namespace Webskt.Common.Abstraction.Interfaces;

public interface IEventProvider
{
    Task<int> GetOrInsertEventIdAsync(string eventName, CancellationToken cancellationToken = default);
    Task InsertEventLogAsync(int eventId, string eventData, DateTime createdAtUtc, CancellationToken cancellationToken = default);
}
