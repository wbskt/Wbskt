using Webskt.Common.Abstraction.Events;
using Webskt.Common.Abstraction.Interfaces;

namespace Webskt.Common.Infrastructure;

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
