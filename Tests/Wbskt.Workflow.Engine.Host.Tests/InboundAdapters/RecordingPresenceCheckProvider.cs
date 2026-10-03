using Wbskt.Workflow.Engine.Host.Providers;

namespace Wbskt.Workflow.Engine.Host.Tests.InboundAdapters;

internal sealed class RecordingPresenceCheckProvider(params string[] triggerKeys) : IClientPresenceCheckProvider
{
    public List<(string KeyPrefix, int WorkspaceId)> KeyLookups { get; } = [];
    public List<(string TriggerKey, Guid ClientRefId, string State, DateTime ChangedAt, DateTime DueAt)> Inserts { get; } = [];
    public List<ClientPresenceCheckRow> Due { get; init; } = [];
    public List<int> Deleted { get; } = [];

    public Task<IReadOnlyCollection<string>> GetTriggerKeysAsync(string keyPrefix, int workspaceId, CancellationToken ct)
    {
        KeyLookups.Add((keyPrefix, workspaceId));
        return Task.FromResult<IReadOnlyCollection<string>>(triggerKeys.Where(k => k.StartsWith(keyPrefix, StringComparison.Ordinal)).ToArray());
    }

    public Task InsertAsync(string triggerKey, Guid clientRefId, string state, DateTime changedAt, DateTime dueAt, CancellationToken ct)
    {
        Inserts.Add((triggerKey, clientRefId, state, changedAt, dueAt));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<ClientPresenceCheckRow>> LeaseDueAsync(int leaseSec, int batch, CancellationToken ct) =>
        Task.FromResult<IReadOnlyCollection<ClientPresenceCheckRow>>(Due);

    public Task DeleteByIdAsync(int id, CancellationToken ct)
    {
        Deleted.Add(id);
        return Task.CompletedTask;
    }
}
