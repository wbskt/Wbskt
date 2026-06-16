using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

public interface ITriggerRegistrationProvider
{
    Task<TriggerRegistrationRow> InsertAsync(TriggerRegistrationRow row, CancellationToken ct);
    Task<IReadOnlyCollection<TriggerRegistrationRow>> GetByTriggerKeyAsync(string triggerKey, CancellationToken ct);
    Task<IReadOnlyCollection<TriggerRegistrationRow>> GetActiveByChannelKeysAsync(string channelKind, IReadOnlyCollection<string> channelKeys, CancellationToken ct)
        => Task.FromResult<IReadOnlyCollection<TriggerRegistrationRow>>(Array.Empty<TriggerRegistrationRow>());
    Task<IReadOnlyCollection<TriggerRegistrationRow>> GetAllByWorkflowDefinitionIdAsync(int workflowDefinitionId, CancellationToken ct);
    Task DeleteAllByWorkflowDefinitionIdAsync(int workflowDefinitionId, CancellationToken ct);
}
