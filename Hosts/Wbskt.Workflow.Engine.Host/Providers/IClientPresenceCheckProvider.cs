namespace Wbskt.Workflow.Engine.Host.Providers;

public interface IClientPresenceCheckProvider
{
    /// <summary>Presence trigger keys starting with <paramref name="keyPrefix"/> on enabled workflows in the workspace.</summary>
    Task<IReadOnlyCollection<string>> GetTriggerKeysAsync(string keyPrefix, int workspaceId, CancellationToken ct);

    /// <summary>Parks a change; a second insert of the same (trigger, change) is a no-op.</summary>
    Task InsertAsync(string triggerKey, Guid clientRefId, string state, DateTime changedAt, DateTime dueAt, CancellationToken ct);

    Task<IReadOnlyCollection<ClientPresenceCheckRow>> LeaseDueAsync(int leaseSec, int batch, CancellationToken ct);

    Task DeleteByIdAsync(int id, CancellationToken ct);
}
