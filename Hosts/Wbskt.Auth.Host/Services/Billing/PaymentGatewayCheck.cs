using Microsoft.Extensions.Options;

namespace Wbskt.Auth.Host.Services.Billing;

/// <summary>
/// Once at start, asks the payment provider whether it accepts this deployment's credentials, so a key
/// for the wrong mode (a live key against the test API) shows up in the logs at deploy rather than at a
/// customer's first checkout. Never stops the host: payments stay as configured either way.
/// </summary>
public sealed class PaymentGatewayCheck(IServiceScopeFactory scopeFactory, IOptions<DodoOptions> dodo, ILogger<PaymentGatewayCheck> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        var gateway = scope.ServiceProvider.GetRequiredService<IPaymentGateway>();
        if (!gateway.IsConfigured)
        {
            logger.LogInformation("Payments are off: {Provider} has no API key and webhook secret here.", gateway.Name);
            return;
        }

        try
        {
            if (await gateway.AcceptsCredentialsAsync(stoppingToken))
            {
                logger.LogInformation("{Provider} accepts this deployment's API key in {Mode}.", gateway.Name, dodo.Value.Environment);
            }
            else
            {
                logger.LogCritical(
                    "{Provider} refuses this deployment's API key in {Mode}: every checkout will fail. Is it a key for the other mode?",
                    gateway.Name, dodo.Value.Environment);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not reach {Provider} to check its API key; checkouts will tell.", gateway.Name);
        }
    }
}
