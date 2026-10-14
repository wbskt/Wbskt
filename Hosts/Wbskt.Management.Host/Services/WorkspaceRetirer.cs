using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Clients;

namespace Wbskt.Management.Host.Services;

/// <summary>
/// Retires what the main database holds for a workspace the auth host has deleted, and does the parts
/// that live outside the database. The one way a workspace is retired, so the
/// <c>WorkspaceDeletedEvent</c> handler and the deleted workspace sweep (<see cref="DeletedWorkspaceSweeper"/>)
/// leave it in the same state.
/// </summary>
/// <remarks>
/// <para>
/// Workspaces live in the auth database, so deleting one cascades to nothing here. This runs
/// <c>dbo.Workspace_Retire</c> - the same end state as disabling each policy, revoking each client
/// and deprecating each workflow by hand - then tells socket hosts to drop the revoked devices, tells
/// the engine to cancel each run still in flight, and tells it to drop what it cached of the
/// workspace's definitions and trigger registrations. The engine owns run state, so the cancels are
/// commands to it, not done here.
/// </para>
/// <para>
/// Safe to repeat: the procedure only reports clients it revoked on this call, and it reports a run
/// for as long as it is still running, so a cancel command that failed to send goes out next time.
/// The engine treats a cancel for a run that is already cancelling, or finished, as a no-op, and
/// clearing its caches twice is harmless. Everything is sent on the real bus, not the queued one: a
/// failure throws, and the caller's retry (the event's redelivery, the sweep's next tick) reports the
/// runs and definitions again.
/// </para>
/// </remarks>
public sealed class WorkspaceRetirer
{
    internal const string CancellationReason = "Workspace deleted.";

    private readonly IWorkspaceRetirementProvider _provider;
    private readonly ClientAccessRevoker _access;
    private readonly IWorkflowEngineGateway _engine;
    private readonly IEventBus _eventBus;
    private readonly ILogger<WorkspaceRetirer> _logger;

    public WorkspaceRetirer(
        IWorkspaceRetirementProvider provider,
        IEventBus eventBus,
        IWorkflowEngineGateway engine,
        IClientTokenCutoffs cutoffs,
        ILogger<WorkspaceRetirer> logger)
    {
        _provider = provider;
        _access = new ClientAccessRevoker(eventBus, cutoffs);
        _engine = engine;
        _eventBus = eventBus;
        _logger = logger;
    }

    /// <summary>Retires the workspace and announces what that changed; returns what the procedure reported.</summary>
    public async Task<WorkspaceRetirement> RetireAsync(int workspaceId, CancellationToken cancellationToken)
    {
        var retired = await _provider.RetireAsync(workspaceId, cancellationToken);

        foreach (var client in retired.RevokedClients)
        {
            await _access.RevokedAsync(workspaceId, client.ClientRefId, client.ClientId, client.PolicyRefId, client.PolicyId, cancellationToken);
        }

        foreach (var runId in retired.ActiveRunIds)
        {
            await _engine.CancelRunAsync(runId, CancellationReason, cancellationToken);
        }

        // This host caches no definitions or registrations; the engine does, and drops them on this event.
        if (retired.DefinitionIds.Count > 0)
        {
            await _eventBus.PublishAsync(new WorkflowDefinitionChangedEvent(null, workspaceId, retired.DefinitionIds), cancellationToken);
        }

        _logger.LogInformation(
            "Retired deleted workspace {WorkspaceId}: revoked {ClientCount} client(s), asked the engine to cancel {RunCount} run(s), disabled {DefinitionCount} definition version(s).",
            workspaceId, retired.RevokedClients.Count, retired.ActiveRunIds.Count, retired.DefinitionIds.Count);

        return retired;
    }
}
