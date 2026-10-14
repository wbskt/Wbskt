using MassTransit;
using Wbskt.Events.Auth;
using Wbskt.Management.Host.Services;

namespace Wbskt.Management.Host.Handlers;

// Retires what the main database holds for a workspace the auth host has deleted; see
// WorkspaceRetirer.
//
// Safe to redeliver: a client is reported only by the call that revoked it, so it is announced once,
// while a run still going and the workspace's definitions are reported every time. A cancel or the
// definition change the broker refuses throws, the event faults, and handling it again sends them.
public sealed class WorkspaceDeletedHandler : IConsumer<WorkspaceDeletedEvent>
{
    private readonly WorkspaceRetirer _retirer;

    public WorkspaceDeletedHandler(WorkspaceRetirer retirer)
    {
        _retirer = retirer;
    }

    public async Task Consume(ConsumeContext<WorkspaceDeletedEvent> context)
    {
        await _retirer.RetireAsync(context.Message.WorkspaceId, context.CancellationToken);
    }
}
