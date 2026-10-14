using MassTransit;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Events.Workflow;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;

namespace Wbskt.Management.Host.Handlers;

// Workspaces live in the auth database, so deleting one cascades to nothing here. This retires what
// the main database holds for it - the same end state as disabling each policy, revoking each client
// and deprecating each workflow by hand - then does the parts that live outside the database:
// telling socket hosts to drop the revoked devices, telling the engine to cancel each run still in
// flight, and telling it to drop what it cached of the workspace's definitions and trigger
// registrations. The engine owns run state, so the cancels are commands to it, not done here.
//
// Safe to redeliver: the procedure only reports clients it revoked on this call, and it reports a run
// for as long as it is still running, so a cancel command that failed to send goes out next time.
// The engine treats a cancel for a run that is already cancelling, or finished, as a no-op, and
// clearing its caches twice is harmless.
public sealed class WorkspaceDeletedHandler : IConsumer<WorkspaceDeletedEvent>
{
    private const string CancellationReason = "Workspace deleted.";

    private readonly IWorkspaceRetirementProvider _provider;
    private readonly ClientAccessRevoker _access;
    private readonly IWorkflowEngineGateway _engine;
    private readonly IEventBus _eventBus;
    private readonly ILogger<WorkspaceDeletedHandler> _logger;

    public WorkspaceDeletedHandler(
        IWorkspaceRetirementProvider provider,
        IEventBus eventBus,
        IWorkflowEngineGateway engine,
        IClientTokenCutoffs cutoffs,
        ILogger<WorkspaceDeletedHandler> logger)
    {
        _provider = provider;
        _access = new ClientAccessRevoker(eventBus, cutoffs);
        _engine = engine;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<WorkspaceDeletedEvent> context)
    {
        var workspaceId = context.Message.WorkspaceId;
        var ct = context.CancellationToken;

        var retired = await _provider.RetireAsync(workspaceId, ct);

        foreach (var client in retired.RevokedClients)
        {
            await _access.RevokedAsync(workspaceId, client.ClientRefId, client.ClientId, client.PolicyRefId, client.PolicyId, ct);
        }

        // Sent now rather than queued: if the broker refuses one, this throws, the event faults, and
        // handling it again reports the same runs again.
        foreach (var runId in retired.ActiveRunIds)
        {
            await _engine.CancelRunAsync(runId, CancellationReason, ct);
        }

        // This host caches no definitions or registrations; the engine does, and drops them on this
        // event. On the real bus, like the cancels: a failure fails the event, and the next delivery
        // reports the same definitions again.
        if (retired.DefinitionIds.Count > 0)
        {
            await _eventBus.PublishAsync(new WorkflowDefinitionChangedEvent(null, workspaceId, retired.DefinitionIds), ct);
        }

        _logger.LogInformation(
            "Retired deleted workspace {WorkspaceId}: revoked {ClientCount} client(s), asked the engine to cancel {RunCount} run(s), disabled {DefinitionCount} definition version(s).",
            workspaceId, retired.RevokedClients.Count, retired.ActiveRunIds.Count, retired.DefinitionIds.Count);
    }
}
