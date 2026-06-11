using System.Text.Json;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class PendingTriggerEventDrainer : IPendingTriggerEventDrainer
{
    private readonly IPendingTriggerEventProvider _pendingTriggerEventProvider;
    private readonly IInboundHub _inboundHub;

    public PendingTriggerEventDrainer(IPendingTriggerEventProvider pendingTriggerEventProvider, IInboundHub inboundHub)
    {
        _pendingTriggerEventProvider = pendingTriggerEventProvider;
        _inboundHub = inboundHub;
    }

    public async Task DrainAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct)
    {
        while (true)
        {
            var row = await _pendingTriggerEventProvider.DequeueNextAsync(workflowRefId, triggerNodeId, correlationKey, ct);
            if (row is null)
            {
                return;
            }

            InboundEvent? evt = JsonSerializer.Deserialize<InboundEvent>(row.InboundEventJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (evt is null)
            {
                throw new InvalidOperationException("Pending trigger event payload could not be deserialized.");
            }

            await _inboundHub.HandleAsync(evt, ct);
        }
    }
}
