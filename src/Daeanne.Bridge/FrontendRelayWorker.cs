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

                var response = await client.PostAsync($"{dispatcherUrl}/tasks", content, stoppingToken);
                var responseBody = await response.Content.ReadAsStringAsync(stoppingToken);

                FrontendResult result;
                if (response.IsSuccessStatusCode)
                {
                    result = new FrontendResult(request.CorrelationId, true, responseBody);
                }
                else
                {
                    result = new FrontendResult(request.CorrelationId, false,
                        Error: $"Dispatcher returned {(int)response.StatusCode}: {responseBody}");
                }

                // Send result to the results queue
                var resultMessage = new ServiceBusMessage(JsonSerializer.Serialize(result, JsonOpts));
                await resultSender.SendMessageAsync(resultMessage, stoppingToken);
                await args.CompleteMessageAsync(args.Message, stoppingToken);

                _logger.LogInformation("FrontendRequest {CorrelationId} processed. Succeeded={Succeeded}",
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
}
