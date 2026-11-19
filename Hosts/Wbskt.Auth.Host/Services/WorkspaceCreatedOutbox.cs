using Wbskt.Events.Auth;
using Wbskt.Infrastructure.Events;
using Wbskt.Infrastructure.Events.Outbox;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// The <see cref="WorkspaceCreatedEvent"/> that <c>dbo.Workspace_Create</c> and <c>dbo.Tenant_Create</c>
/// write to the outbox in their own transaction, so the management host hears of every workspace even
/// when the broker is down. The procedure fills in the workspace's id and reference, which only it
/// knows; the actor is stamped here, while the request is still in scope.
/// </summary>
internal sealed class WorkspaceCreatedOutbox(IIdentityService identity, IRequestOriginAccessor? requestOrigin = null)
{
    public OutboxMessage For(string workspaceName)
    {
        var created = new WorkspaceCreatedEvent(0, Guid.Empty, workspaceName);
        ActorStampingEventBus.Stamp(created, identity, requestOrigin);
        return OutboxMessage.Create(created);
    }
}
