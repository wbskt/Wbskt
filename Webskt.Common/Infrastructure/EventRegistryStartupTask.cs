using Webskt.Common.Abstraction.Interfaces;

namespace Webskt.Common.Infrastructure;

internal sealed class EventRegistryStartupTask : IStartupTask
{
    private readonly IEventRegistry _eventRegistry;

    public EventRegistryStartupTask(IEventRegistry eventRegistry)
    {
        _eventRegistry = eventRegistry;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await _eventRegistry.InitializeAsync(cancellationToken);
    }
}
