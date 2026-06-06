using MassTransit;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

internal static class InboundMessageId
{
    /// <summary>
    /// Returns a stable id for an inbound message. The bus assigns a <c>MessageId</c> at publish
    /// time that survives transport redeliveries, so an idempotency key derived from it dedupes a
    /// redelivered message to a single run. Falls back to a fresh id only if the bus did not set one.
    /// </summary>
    public static string Stable(ConsumeContext context)
    {
        return context.MessageId?.ToString() ?? Guid.NewGuid().ToString();
    }
}
