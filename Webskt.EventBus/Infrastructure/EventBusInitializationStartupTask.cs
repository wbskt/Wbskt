using Webskt.Common.Abstraction.Interfaces;
using Webskt.EventBus.Abstractions;

namespace Webskt.EventBus.Infrastructure;

public sealed class EventBusInitializationStartupTask : IStartupTask
{
    private readonly IEventBus _eventBus;

    public EventBusInitializationStartupTask(IEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await _eventBus.InitializeAsync(cancellationToken);
    }
}
