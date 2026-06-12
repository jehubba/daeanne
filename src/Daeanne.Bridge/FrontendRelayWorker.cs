using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
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
        var resultQueue = _config["Bridge:FrontendResultQueue"] ?? "daeanne-frontend-results";

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogWarning("FrontendRelayWorker disabled — no ServiceBus connection string.");
            await Task.Delay(Timeout.Infinite, stoppingToken);
            return;
        }

        _logger.LogInformation("FrontendRelayWorker starting. Queue: {Queue}", requestQueue);

        await using var sbClient = new ServiceBusClient(connectionString);
        await using var resultSender = sbClient.CreateSender(resultQueue);

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
                    await SendResultAsync(resultSender, errorResult, stoppingToken);
                    await args.CompleteMessageAsync(args.Message, stoppingToken);
                    return;
                }

                // Extract task ID from the creation response
                var createdTask = JsonSerializer.Deserialize<JsonElement>(createBody, JsonOpts);
                var taskId = createdTask.GetProperty("id").GetString();

                _logger.LogInformation("Task created: {TaskId} for {CorrelationId}", taskId, request.CorrelationId);

                // Poll for task completion (up to 10 minutes)
                var result = await PollForCompletionAsync(client, dispatcherUrl, taskId!, request.CorrelationId, stoppingToken);

                await SendResultAsync(resultSender, result, stoppingToken);
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

    private static async Task SendResultAsync(ServiceBusSender sender, FrontendResult result, CancellationToken ct)
    {
        var message = new ServiceBusMessage(JsonSerializer.Serialize(result, JsonOpts));
        await sender.SendMessageAsync(message, ct);
    }
}
