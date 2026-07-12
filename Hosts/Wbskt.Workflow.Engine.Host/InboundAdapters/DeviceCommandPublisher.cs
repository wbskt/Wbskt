using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

public sealed class DeviceCommandPublisher(IEventBus eventBus) : IDeviceCommandPublisher
{
    public async Task PublishCommandAsync(Guid clientRefId, int clientId, int workspaceId, string command, string payload, CancellationToken ct)
    {
        await eventBus.PublishAsync(new ClientCommandEvent(clientRefId, clientId, workspaceId, command, payload), ct);
    }
}
