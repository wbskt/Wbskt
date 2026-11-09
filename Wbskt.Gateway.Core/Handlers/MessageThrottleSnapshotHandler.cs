using MassTransit;
using Wbskt.Events.Billing;
using Wbskt.Gateway.Core.Infrastructure;

namespace Wbskt.Gateway.Core.Handlers;

/// <summary>Takes the management host's list of throttled workspaces (per-instance fan-out, so every socket host gets it).</summary>
public sealed class MessageThrottleSnapshotHandler(MessageThrottle throttle) : IConsumer<MessageThrottleSnapshot>
{
    public Task Consume(ConsumeContext<MessageThrottleSnapshot> context)
    {
        throttle.Replace(context.Message.WorkspaceIds, context.Message.TakenAtUtc);
        return Task.CompletedTask;
    }
}
