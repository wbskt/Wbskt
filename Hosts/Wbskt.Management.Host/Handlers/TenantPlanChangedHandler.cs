using MassTransit;
using Wbskt.Events.Auth;
using Wbskt.Management.Host.Services.Billing;

namespace Wbskt.Management.Host.Handlers;

// Re-copies the tenant's workspace accounts from the auth host. The copy is read fresh rather than
// taken from the event, so events handled out of order, or twice, still leave the latest plan.
// A failure (the auth host down) faults the message for the bus to retry; the periodic sync is the
// backstop if it never succeeds.
public sealed class TenantPlanChangedHandler(WorkspaceAccountSync sync) : IConsumer<TenantPlanChangedEvent>
{
    public async Task Consume(ConsumeContext<TenantPlanChangedEvent> context)
    {
        await sync.SyncTenantAsync(context.Message.TenantId, context.CancellationToken);
    }
}
