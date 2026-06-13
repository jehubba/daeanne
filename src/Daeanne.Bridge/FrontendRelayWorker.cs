using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Daeanne.Shared.Models;

namespace Daeanne.Bridge;

public class FrontendRelayWorker : BackgroundService
{
    private readonly ILogger<FrontendRelayWorker> _logger;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _http;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public FrontendRelayWorker(ILogger<FrontendRelayWorker> logger, IConfiguration config, IHttpClientFactory http)
    {
        _logger = logger;
        _config = config;
        _http = http;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = _config.GetConnectionString("ServiceBus");
        var dispatcherUrl = _config["Bridge:DispatcherUrl"] ?? "http://127.0.0.1:47777";
        var requestQueue = _config["Bridge:FrontendRequestQueue"] ?? "daeanne-frontend-requests";
        var blobConnStr = _config.GetConnectionString("FrontendStorage");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogWarning("FrontendRelayWorker disabled — no ServiceBus connection string.");
            await Task.Delay(Timeout.Infinite, stoppingToken);
            return;
        }

        BlobContainerClient? blobContainer = null;
        if (!string.IsNullOrWhiteSpace(blobConnStr))
        {
            blobContainer = new BlobContainerClient(blobConnStr, "frontend-results");
            await blobContainer.CreateIfNotExistsAsync(cancellationToken: stoppingToken);
            _logger.LogInformation("FrontendRelayWorker: blob result store ready.");
        }
        else
        {
            _logger.LogWarning("FrontendRelayWorker: no FrontendStorage connection string — results will not be persisted.");
        }

        _logger.LogInformation("FrontendRelayWorker starting. Queue: {Queue}", requestQueue);

        await using var sbClient = new ServiceBusClient(connectionString);

        var options = new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 2,
            AutoCompleteMessages = false
        };

        await using var processor = sbClient.CreateProcessor(requestQueue, options);

        processor.ProcessMessageAsync += async args =>
        {
            try
            {
                var request = JsonSerializer.Deserialize<FrontendRequest>(args.Message.Body.ToString(), JsonOpts);
                if (request is null)
                {
                    _logger.LogWarning("Received null FrontendRequest, completing message.");
                    await args.CompleteMessageAsync(args.Message, stoppingToken);
                    return;
                }

                _logger.LogInformation("Processing FrontendRequest: {CorrelationId} Type={TaskType}",
                    request.CorrelationId, request.TaskType);

                // Forward to Dispatcher as a task creation request
                var client = _http.CreateClient("dispatcher");
                var taskPayload = new
                {
                    Type = request.TaskType,
                    Prompt = request.Prompt,
                    CorrelationId = request.CorrelationId
                };
                var content = new StringContent(
                    JsonSerializer.Serialize(taskPayload, JsonOpts),
                    Encoding.UTF8,
                    "application/json");

                var createResponse = await client.PostAsync($"{dispatcherUrl}/tasks", content, stoppingToken);
                var createBody = await createResponse.Content.ReadAsStringAsync(stoppingToken);

                if (!createResponse.IsSuccessStatusCode)
                {
                    var errorResult = new FrontendResult(request.CorrelationId, false,
                        Error: $"Dispatcher returned {(int)createResponse.StatusCode}: {createBody}");
                        await SaveResultAsync(blobContainer, errorResult, stoppingToken);
                    await args.CompleteMessageAsync(args.Message, stoppingToken);
                    return;
                }

                // Extract task ID from the creation response
                var createdTask = JsonSerializer.Deserialize<JsonElement>(createBody, JsonOpts);
                var taskId = createdTask.GetProperty("id").GetString();

                _logger.LogInformation("Task created: {TaskId} for {CorrelationId}", taskId, request.CorrelationId);

                // Poll for task completion (up to 10 minutes)
                var result = await PollForCompletionAsync(client, dispatcherUrl, taskId!, request.CorrelationId, stoppingToken);

                await SaveResultAsync(blobContainer, result, stoppingToken);
                await PostPushNotifyAsync(result, stoppingToken);
                await args.CompleteMessageAsync(args.Message, stoppingToken);

                _logger.LogInformation("FrontendRequest {CorrelationId} completed. Succeeded={Succeeded}",
                    request.CorrelationId, result.Succeeded);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing FrontendRequest");
            }
        };

        processor.ProcessErrorAsync += args =>
        {
            _logger.LogError(args.Exception, "FrontendRelayWorker error. Source={Source}", args.ErrorSource);
            return Task.CompletedTask;
        };

        await processor.StartProcessingAsync(stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Succeeded", "Partial", "Failed", "TimedOut", "Escalated"
    };

    private async Task<FrontendResult> PollForCompletionAsync(
        HttpClient client, string dispatcherUrl, string taskId, string correlationId,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMinutes(10);

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(3000, ct);

            try
            {
                var response = await client.GetAsync($"{dispatcherUrl}/tasks/{taskId}", ct);
                if (!response.IsSuccessStatusCode) continue;

                var body = await response.Content.ReadAsStringAsync(ct);
                var task = JsonSerializer.Deserialize<JsonElement>(body, JsonOpts);

                var status = task.GetProperty("status").GetString() ?? "";

                if (!TerminalStatuses.Contains(status)) continue;

                var resultJson = task.TryGetProperty("resultJson", out var rj) ? rj.GetString() : null;
                var error = task.TryGetProperty("error", out var err) ? err.GetString() : null;
                var succeeded = status is "Succeeded" or "Partial";

                // ResultJson is often a JSON object — extract a readable response string
                var responseText = ExtractResponseText(resultJson);

                return new FrontendResult(correlationId, succeeded, responseText, error);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Poll failed for task {TaskId}", taskId);
            }
        }

        return new FrontendResult(correlationId, false,
            Error: "Task did not complete within 10 minutes.");
    }

    /// <summary>
    /// ResultJson from the Dispatcher can be a JSON object like {"response":"..."} or a plain string.
    /// Extract the most useful human-readable text.
    /// </summary>
    private static string? ExtractResponseText(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson)) return null;

        try
        {
            var doc = JsonDocument.Parse(resultJson);
            var root = doc.RootElement;

            // Try common response field names
            foreach (var field in new[] { "response", "result", "message", "summary", "output" })
            {
                if (root.TryGetProperty(field, out var prop) && prop.ValueKind == JsonValueKind.String)
                    return prop.GetString();
            }

            // If it's a simple string value at root, use it
            if (root.ValueKind == JsonValueKind.String)
                return root.GetString();

            // Fall back to the full JSON
            return resultJson;
        }
        catch (JsonException)
        {
            // Not valid JSON — treat as plain text
            return resultJson;
        }
    }

    private async Task SaveResultAsync(BlobContainerClient? container, FrontendResult result, CancellationToken ct)
    {
        if (container is null)
        {
            _logger.LogWarning("FrontendRelayWorker: no blob container — dropping result for {CorrelationId}", result.CorrelationId);
            return;
        }

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var blob = container.GetBlobClient($"{result.CorrelationId}.json");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await blob.UploadAsync(stream, overwrite: true, cancellationToken: ct);
        _logger.LogInformation("FrontendRelayWorker: result saved to blob for {CorrelationId}", result.CorrelationId);
    }

    /// <summary>
    /// Posts a push notification to the Frontend API's /api/notify endpoint
    /// when a task completes.  Silently skips if FrontendApiUrl is not configured.
    /// </summary>
    private async Task PostPushNotifyAsync(FrontendResult result, CancellationToken ct)
    {
        var frontendApiUrl = _config["Bridge:FrontendApiUrl"];
        if (string.IsNullOrWhiteSpace(frontendApiUrl)) return;

        try
        {
            var internalKey = _config["Bridge:FrontendInternalKey"] ?? "";
            var notifyType = result.Succeeded ? "task_complete" : "alert";
            var title = result.Succeeded ? "Task completed" : "Task failed";
            var body = result.Succeeded
                ? TruncateMessage(result.Response, "Your task finished successfully.")
                : TruncateMessage(result.Error, "A task did not complete.");

            var payload = new
            {
                type = notifyType,
                title,
                body,
                taskId = (string?)null,
                url = "/tasks"
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var client = _http.CreateClient("frontend");
            var request = new HttpRequestMessage(HttpMethod.Post, $"{frontendApiUrl.TrimEnd('/')}/notify")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(internalKey))
                request.Headers.Add("X-Internal-Key", internalKey);

            var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("FrontendRelayWorker: notify returned {Status}.", response.StatusCode);
            else
                _logger.LogInformation("FrontendRelayWorker: push notification sent.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FrontendRelayWorker: failed to send push notification.");
        }
    }

    private static string TruncateMessage(string? message, string defaultText, int maxLength = 120)
        => message is { Length: > 0 } m ? m[..Math.Min(m.Length, maxLength)] : defaultText;
}
