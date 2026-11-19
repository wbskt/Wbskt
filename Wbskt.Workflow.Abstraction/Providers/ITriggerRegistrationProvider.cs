using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

public interface ITriggerRegistrationProvider
{
    /// <param name="workspaceId">When set, only registrations of that workspace's workflows (<see cref="Runtime.InboundEvent.WorkspaceId"/>).</param>
    Task<IReadOnlyCollection<TriggerRegistrationRow>> GetActiveByChannelKeysAsync(string channelKind, IReadOnlyCollection<string> channelKeys, int? workspaceId, CancellationToken ct)
        => Task.FromResult<IReadOnlyCollection<TriggerRegistrationRow>>(Array.Empty<TriggerRegistrationRow>());
}
