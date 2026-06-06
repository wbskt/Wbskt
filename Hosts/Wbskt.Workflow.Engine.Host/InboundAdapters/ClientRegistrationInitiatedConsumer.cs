using System.Text.Json;
using MassTransit;
using Wbskt.Events.Management;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

public sealed class ClientRegistrationInitiatedConsumer(IInboundHub hub) : IConsumer<ClientRegistrationInitiatedEvent>
{
    public Task Consume(ConsumeContext<ClientRegistrationInitiatedEvent> context)
    {
        ClientRegistrationInitiatedEvent evt = context.Message;
        InboundEvent inboundEvent = new(
            "client-registered",
            $"client:{evt.ClientRefId}",
            $"client-registered:{evt.ClientRefId}:{InboundMessageId.Stable(context)}",
            new Dictionary<string, JsonElement>
            {
                ["clientRefId"] = JsonSerializer.SerializeToElement(evt.ClientRefId),
                ["clientId"] = JsonSerializer.SerializeToElement(evt.ClientId),
                ["policyRefId"] = JsonSerializer.SerializeToElement(evt.PolicyRefId),
                ["policyId"] = JsonSerializer.SerializeToElement(evt.PolicyId),
                ["workspaceId"] = JsonSerializer.SerializeToElement(evt.WorkspaceId),
                ["name"] = JsonSerializer.SerializeToElement(evt.Name)
            },
            default);

        return hub.HandleAsync(inboundEvent, context.CancellationToken);
    }
}
