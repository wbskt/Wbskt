using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Primitives;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

public sealed class DeviceCommandPublisher(
    IEventBus eventBus,
    [FromKeyedServices(ReferenceType.Client)] IReferenceMapper clientMapper,
    ILogger<DeviceCommandPublisher> logger) : IDeviceCommandPublisher
{
    public async Task PublishCommandAsync(Guid clientRefId, int workspaceId, string command, string payload, CancellationToken ct)
    {
        // EventLogs attributes comms rows to the internal client id; resolve it here so
        // workflow-sent commands show up in the client's comms history.
        var clientId = await clientMapper.FindIdByRefIdAsync(clientRefId, ct);
        if (clientId <= 0)
        {
            logger.LogWarning("No client found for ref id '{ClientRefId}'; command '{Command}' will be logged without a client id.", clientRefId, command);
        }

        await eventBus.PublishAsync(new ClientCommandEvent(clientRefId, clientId, workspaceId, command, payload, Guid.NewGuid()), ct);
    }
}
