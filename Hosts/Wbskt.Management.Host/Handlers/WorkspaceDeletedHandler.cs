using MassTransit;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;

namespace Wbskt.Management.Host.Handlers;

// Workspaces live in the auth database, so deleting one cascades to nothing here. This retires what
// the main database holds for it - the same end state as disabling each policy, revoking each client
// and deprecating each workflow by hand - then does the parts that live outside the database:
// telling socket hosts to drop the revoked devices, and telling the engine to cancel each run still
// in flight. The engine owns run state, so the cancels are commands to it, not done here.
//
// Safe to redeliver: the procedure only reports clients it revoked on this call, and it reports a run
// for as long as it is still running, so a cancel command that failed to send goes out next time.
// The engine treats a cancel for a run that is already cancelling, or finished, as a no-op.
public sealed class WorkspaceDeletedHandler : IConsumer<WorkspaceDeletedEvent>
{
    private const string CancellationReason = "Workspace deleted.";

    private readonly IWorkspaceRetirementProvider _provider;
    private readonly ClientAccessRevoker _access;
    private readonly IWorkflowEngineGateway _engine;
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

        // The disabled definitions are not evicted from any cache here: this host has none, and the
        // engine's is its own. A WorkflowDefinitionChanged event for the engine is the planned follow-up.
        _logger.LogInformation(
            "Retired deleted workspace {WorkspaceId}: revoked {ClientCount} client(s), asked the engine to cancel {RunCount} run(s), disabled {DefinitionCount} definition version(s).",
            workspaceId, retired.RevokedClients.Count, retired.ActiveRunIds.Count, retired.DefinitionIds.Count);
    }
}
