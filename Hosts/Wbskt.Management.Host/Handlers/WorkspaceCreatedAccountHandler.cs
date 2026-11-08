using MassTransit;
using Wbskt.Events.Auth;
using Wbskt.Management.Host.Services.Billing;

namespace Wbskt.Management.Host.Handlers;

// Copies a new workspace's account at once, so a workspace added to a paying tenant is not held to
// Free until the next full sync. The event carries no tenant, so the copy comes from the auth host.
public sealed class WorkspaceCreatedAccountHandler(WorkspaceAccountSync sync) : IConsumer<WorkspaceCreatedEvent>
{
    public async Task Consume(ConsumeContext<WorkspaceCreatedEvent> context)
    {
        await sync.SyncWorkspaceAsync(context.Message.WorkspaceId, context.CancellationToken);
    }
}
