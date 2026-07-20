using Wbskt.EventBus.Abstractions;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Events.Hosting;

namespace Wbskt.Socket.Host.HostedServices;

// Announces this host's start so the management host can clear presence flags left behind
// by a hard crash (connections that never got a ClientDisconnectedEvent).
internal sealed class StartupPresenceResetPublisher : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private const int MaxAttempts = 3;

    private readonly IEventBus _eventBus;
    private readonly ILogger<StartupPresenceResetPublisher> _logger;
    private readonly string _hostId;

    public StartupPresenceResetPublisher(IEventBus eventBus, ILogger<StartupPresenceResetPublisher> logger, BusInstanceId busInstanceId)
    {
        _eventBus = eventBus;
        _logger = logger;
        _hostId = busInstanceId.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                await _eventBus.PublishAsync(new SocketHostStartedEvent(_hostId), stoppingToken);
                _logger.LogInformation("Published SocketHostStartedEvent (presence reset).");
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // The bus may not be fully started yet on the very first attempt.
                _logger.LogWarning("Failed to publish SocketHostStartedEvent (attempt {Attempt}/{Max}): {Message}", attempt, MaxAttempts, ex.Message);
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(RetryDelay, stoppingToken);
                }
            }
        }
    }
}
