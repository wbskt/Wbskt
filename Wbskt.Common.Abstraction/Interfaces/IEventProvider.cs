namespace Wbskt.Common.Abstraction.Interfaces;

public interface IEventProvider
{
    Task<int> GetOrInsertEventIdAsync(string eventName, short criticality, CancellationToken cancellationToken = default);
    Task InsertEventLogAsync(int eventId, string eventData, DateTime createdAtUtc, int? workspaceId, CancellationToken cancellationToken = default);
}
