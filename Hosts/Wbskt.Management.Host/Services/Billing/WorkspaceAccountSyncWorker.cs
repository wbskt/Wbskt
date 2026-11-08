using Microsoft.Extensions.Options;
using Wbskt.Infrastructure.Hosting;
using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Services.Billing;

/// <summary>
/// The backstop behind <c>TenantPlanChangedEvent</c> and <c>WorkspaceCreatedEvent</c>: copies every
/// workspace account from the auth host at start and then every
/// <see cref="BillingOptions.AccountSyncInterval"/>, so a copy an event never reached (a lost event, a
/// tenant created before billing, a handler that gave up) is right by the next sync. Each management
/// replica runs it; the copies are written by revision, so two at once only repeat work.
/// </summary>
public sealed class WorkspaceAccountSyncWorker(IServiceScopeFactory scopeFactory, IOptions<BillingOptions> options, ILogger<WorkspaceAccountSyncWorker> logger)
    : PeriodicWorker(AlwaysOpenWorkGate.Instance, scopeFactory, logger)
{
    protected override string Name => "Workspace account sync";

    protected override TimeSpan Interval { get; } = options.Value.AccountSyncInterval;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        await services.GetRequiredService<WorkspaceAccountSync>().SyncAllAsync(ct);
    }
}
