using Wbskt.EventBus.Abstractions;

namespace Wbskt.Management.Host.Services.Admin;

/// <summary>
/// Holds what a staff change publishes until the change has committed. The change runs inside the staff
/// runner's transaction, and the host's bus sends at once, so an event published in there could go out
/// for a change that is then rolled back.
/// </summary>
internal sealed class DeferredEventBus : IEventBus
{
    private readonly List<Func<IEventBus, CancellationToken, Task>> _held = [];

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
    {
        // Kept as a typed call: the bus routes on the event's static type.
        _held.Add((bus, token) => bus.PublishAsync(@event, token));
        return Task.CompletedTask;
    }

    /// <summary>Publishes everything held, in order, on <paramref name="bus"/>.</summary>
    public async Task ReleaseAsync(IEventBus bus, CancellationToken ct)
    {
        foreach (var publish in _held)
        {
            await publish(bus, ct);
        }

        _held.Clear();
    }
}
