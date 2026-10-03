using MassTransit;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Events.Client;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Management.Host.Handlers;

// Workspaces live in the auth database, so deleting one cascades to nothing here. This retires what
// the main database holds for it - the same end state as disabling each policy, revoking each client
// and deprecating each workflow by hand - then does the parts that live outside the database:
// telling socket hosts to drop the revoked devices, cancelling runs still in flight, and evicting
// the disabled definitions from this host's cache.
//
// Safe to redeliver: the procedure only reports clients it revoked on this call, and asking to
// cancel a run that is already cancelling is a no-op.
public sealed class WorkspaceDeletedHandler : IConsumer<WorkspaceDeletedEvent>
{
    private const string CancellationReason = "Workspace deleted.";

    private readonly IWorkspaceRetirementProvider _provider;
    private readonly IEventBus _eventBus;
    private readonly IRunCancellationService _runCancellation;
    private readonly IWorkflowDefinitionCache _definitionCache;
    private readonly ILogger<WorkspaceDeletedHandler> _logger;

    public WorkspaceDeletedHandler(
        IWorkspaceRetirementProvider provider,
        IEventBus eventBus,
        IRunCancellationService runCancellation,
        IWorkflowDefinitionCache definitionCache,
        ILogger<WorkspaceDeletedHandler> logger)
    {
        _provider = provider;
        _eventBus = eventBus;
        _runCancellation = runCancellation;
        _definitionCache = definitionCache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<WorkspaceDeletedEvent> context)
    {
        var workspaceId = context.Message.WorkspaceId;
        var ct = context.CancellationToken;

        var retired = await _provider.RetireAsync(workspaceId, ct);

        foreach (var client in retired.RevokedClients)
        {
            await _eventBus.PublishAsync(new ClientStatusChangedEvent(
                client.ClientRefId, client.ClientId, client.PolicyRefId, client.PolicyId, workspaceId, (byte)ClientStatus.Revoked), ct);
        }

        foreach (var runId in retired.ActiveRunIds)
        {
            await _runCancellation.RequestCancellationAsync(runId, CancellationReason, ct);
        }

        foreach (var definitionId in retired.DefinitionIds)
        {
            _definitionCache.Invalidate(definitionId);
        }

        _logger.LogInformation(
            "Retired deleted workspace {WorkspaceId}: revoked {ClientCount} client(s), cancelling {RunCount} run(s), disabled {DefinitionCount} definition version(s).",
            workspaceId, retired.RevokedClients.Count, retired.ActiveRunIds.Count, retired.DefinitionIds.Count);
    }
}
