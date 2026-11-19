using MassTransit;
using Wbskt.Events.Auth;
using Wbskt.Management.Host.Services;

namespace Wbskt.Management.Host.Handlers;

// Carries a freeze into the tenant's workspaces; see WorkspaceFreezer. Safe to redeliver: the account
// copy is read fresh, a client is announced only by the call that revoked it, and a run still going is
// cancelled again, which the engine ignores.
public sealed class TenantFrozenHandler(WorkspaceFreezer freezer) : IConsumer<TenantFrozenEvent>
{
    public async Task Consume(ConsumeContext<TenantFrozenEvent> context)
    {
        await freezer.FreezeAsync(context.Message.WorkspaceIds, context.CancellationToken);
    }
}

// Carries the end of a freeze into the tenant's workspaces, so runs and enrolments are allowed again.
public sealed class TenantUnfrozenHandler(WorkspaceFreezer freezer) : IConsumer<TenantUnfrozenEvent>
{
    public async Task Consume(ConsumeContext<TenantUnfrozenEvent> context)
    {
        await freezer.UnfreezeAsync(context.Message.WorkspaceIds, context.CancellationToken);
    }
}
