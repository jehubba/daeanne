namespace Daeanne.Bridge;

/// <summary>
/// Bidirectional bridge between Azure Service Bus and the local Kestrel dispatcher.
///
/// Inbound:  Service Bus inbound queue → HTTP POST to Dispatcher /tasks
/// Outbound: Dispatcher /outbox/email  → Service Bus outbound queue
///
/// Phase 1: Shell only. Runs in disabled mode when connection string is absent.
/// Phase 2+: Wire up Azure.Messaging.ServiceBus processor and sender.
/// </summary>
public class BridgeWorker : BackgroundService
{
    private readonly ILogger<BridgeWorker> _logger;
    private readonly IConfiguration _config;

    public BridgeWorker(ILogger<BridgeWorker> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = _config.GetConnectionString("ServiceBus");
        var dispatcherUrl = _config["Bridge:DispatcherUrl"] ?? "http://127.0.0.1:47777";

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogWarning(
                "Daeanne.Bridge is running in DISABLED mode. " +
                "Service Bus connection string is not configured. " +
                "Set ConnectionStrings:ServiceBus to enable inbound/outbound email bridging.");

            // Stay alive but do nothing until stopped
            await Task.Delay(Timeout.Infinite, stoppingToken);
            return;
        }

        _logger.LogInformation(
            "Daeanne.Bridge starting. Dispatcher: {Url}", dispatcherUrl);

        // TODO (Phase 2): Initialize ServiceBusClient, start inbound processor,
        //   and start outbound polling loop.
        _logger.LogWarning(
            "Daeanne.Bridge: Service Bus connection string is set but bridging " +
            "is not yet implemented. This is a Phase 1 shell.");

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
