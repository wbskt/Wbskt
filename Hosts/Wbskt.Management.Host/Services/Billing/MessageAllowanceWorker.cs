using Microsoft.Extensions.Options;
using Wbskt.Infrastructure.Hosting;
using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Services.Billing;

/// <summary>
/// Runs <see cref="MessageAllowanceMonitor"/> every <see cref="BillingOptions.MessageAllowanceInterval"/>.
/// Each management replica runs it: the socket hosts take the latest list from whichever sends it, and
/// the limit's event is recorded once per workspace and month in the database.
/// </summary>
public sealed class MessageAllowanceWorker(IServiceScopeFactory scopeFactory, IOptions<BillingOptions> options, ILogger<MessageAllowanceWorker> logger)
    : PeriodicWorker(AlwaysOpenWorkGate.Instance, scopeFactory, logger)
{
    protected override string Name => "Message allowance";

    protected override TimeSpan Interval { get; } = options.Value.MessageAllowanceInterval;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        await services.GetRequiredService<MessageAllowanceMonitor>().CheckAsync(ct);
    }
}
