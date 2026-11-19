using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Billing;
using Wbskt.Management.Host.Services.Clients;

namespace Wbskt.Management.Host.Services;

/// <summary>
/// Carries a freeze (or its end) from the auth host into the main database for each of the tenant's
/// workspaces, and stops what is going in a frozen one.
/// </summary>
/// <remarks>
/// <para>
/// The freeze itself is copied with the workspace's account (<c>dbo.WorkspaceAccounts.FrozenAt</c>), read
/// fresh from the auth host like a plan change, so <c>dbo.Run_Start</c> and <c>dbo.Client_Create</c>
/// refuse from then on and the periodic account sync repairs a copy this missed. A frozen workspace then
/// has its live clients revoked, and socket hosts told to drop them, and the engine is asked to cancel
/// each run still going. The engine owns run state, so the cancels are commands to it.
/// </para>
/// <para>
/// Safe to repeat, as <see cref="WorkspaceRetirer"/> is: a client is reported only by the call that
/// revoked it, a run for as long as it is going, and a cancel for a run already cancelling is a no-op.
/// Everything is sent on the real bus, so a failure throws and the event's redelivery tries again.
/// </para>
/// </remarks>
public sealed class WorkspaceFreezer
{
    internal const string CancellationReason = "Account frozen by Wbskt support.";

    private readonly WorkspaceAccountSync _accounts;
    private readonly IWorkspaceFreezeProvider _provider;
    private readonly ClientAccessRevoker _access;
    private readonly IWorkflowEngineGateway _engine;
    private readonly ILogger<WorkspaceFreezer> _logger;

    public WorkspaceFreezer(
        WorkspaceAccountSync accounts,
        IWorkspaceFreezeProvider provider,
        IEventBus eventBus,
        IWorkflowEngineGateway engine,
        IClientTokenCutoffs cutoffs,
        ILogger<WorkspaceFreezer> logger)
    {
        _accounts = accounts;
        _provider = provider;
        _access = new ClientAccessRevoker(eventBus, cutoffs);
        _engine = engine;
        _logger = logger;
    }

    /// <summary>Copies the freeze to each workspace, then revokes its clients and cancels its runs.</summary>
    public async Task FreezeAsync(IReadOnlyCollection<int> workspaceIds, CancellationToken cancellationToken)
    {
        foreach (var workspaceId in workspaceIds)
        {
            await _accounts.SyncWorkspaceAsync(workspaceId, cancellationToken);
            var frozen = await _provider.FreezeAsync(workspaceId, cancellationToken);

            foreach (var client in frozen.RevokedClients)
            {
                await _access.RevokedAsync(workspaceId, client.ClientRefId, client.ClientId, client.PolicyRefId, client.PolicyId, cancellationToken);
            }

            foreach (var runId in frozen.ActiveRunIds)
            {
                await _engine.CancelRunAsync(runId, CancellationReason, cancellationToken);
            }

            _logger.LogInformation(
                "Froze workspace {WorkspaceId}: revoked {ClientCount} client(s), asked the engine to cancel {RunCount} run(s).",
                workspaceId, frozen.RevokedClients.Count, frozen.ActiveRunIds.Count);
        }
    }

    /// <summary>Copies the end of the freeze to each workspace. Revoked clients stay revoked until approved again.</summary>
    public async Task UnfreezeAsync(IReadOnlyCollection<int> workspaceIds, CancellationToken cancellationToken)
    {
        foreach (var workspaceId in workspaceIds)
        {
            await _accounts.SyncWorkspaceAsync(workspaceId, cancellationToken);
        }
    }
}
