using Wbskt.Common.Events;
using Wbskt.EventBus;
using Wbskt.Workflow.Api.EventHandlers;

namespace Wbskt.Workflow.Api.HostedServices;

public class RealtimeTriggerService : IHostedService
{
    private readonly IEventBus _eventBus;

    public RealtimeTriggerService(IEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _eventBus.Subscribe<ClientDataReceivedEvent, ClientDataReceivedEventHandler>();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
