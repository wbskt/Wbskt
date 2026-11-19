using Wbskt.Models;
using Wbskt.Infrastructure;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Management.Host.Services.Workflow;

public sealed class TriggerActivityService : ITriggerActivityService
{
    private readonly IWorkflowDefinitionProvider _workflowDefinitionProvider;
    private readonly ITriggerDispatchOutcomeProvider _outcomeProvider;
    private readonly ILogger<TriggerActivityService> _logger;

    public TriggerActivityService(IWorkflowDefinitionProvider workflowDefinitionProvider, ITriggerDispatchOutcomeProvider outcomeProvider, ILogger<TriggerActivityService> logger)
    {
        _workflowDefinitionProvider = workflowDefinitionProvider;
        _outcomeProvider = outcomeProvider;
        _logger = logger;
    }

    public async Task<Result<TriggerActivityResponse>> GetRecentAsync(int workspaceId, Guid workflowRefId, Guid triggerNodeId, CancellationToken ct)
    {
        var ensureWorkflowResult = await WorkflowOwnershipCheck.EnsureCurrentAsync(_workflowDefinitionProvider, _logger, workspaceId, workflowRefId, ct);
        if (ensureWorkflowResult.IsFailure)
        {
            return Result<TriggerActivityResponse>.Failure(ensureWorkflowResult.Error);
        }

        IReadOnlyCollection<TriggerDispatchOutcomeRow> outcomes = await _outcomeProvider.GetRecentAsync(workflowRefId, triggerNodeId, ITriggerDispatchOutcomeProvider.Keep, ct);
        TriggerHoldStateRow? hold = await _outcomeProvider.FindHoldStateAsync(workflowRefId, triggerNodeId, ct);

        return Result<TriggerActivityResponse>.Success(new TriggerActivityResponse(
            outcomes.Select(o => new TriggerOutcomeDto(o.OccurredAt.ToUtcOffset(), o.ChannelKind, o.MessageType, o.Outcome, o.CorrelationKey, o.RunRefId)).ToArray(),
            hold is null ? null : new TriggerHoldStateDto(hold.State, hold.SinceAt.ToUtcOffset(), hold.DueAt.ToUtcOffset(), hold.LastEventAt.ToUtcOffset())));
    }
}
