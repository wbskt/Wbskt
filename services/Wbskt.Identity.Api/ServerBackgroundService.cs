namespace Wbskt.Identity.Api;

public class ServerBackgroundService(ILogger<ServerBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("server background Service is starting...");
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested) { }
    }
}
