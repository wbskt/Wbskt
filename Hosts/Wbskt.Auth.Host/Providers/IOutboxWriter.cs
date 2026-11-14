using Wbskt.Infrastructure.Events.Outbox;

namespace Wbskt.Auth.Host.Providers;

/// <summary>Writes an event to the auth database's outbox by itself, for a change that is only the event.</summary>
internal interface IOutboxWriter
{
    Task AddAsync(OutboxMessage message, CancellationToken cancellationToken);
}
