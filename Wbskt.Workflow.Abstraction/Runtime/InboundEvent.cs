using System.Text.Json;

namespace Wbskt.Workflow.Abstraction.Runtime;

public sealed record InboundEvent(
    string ChannelKind,
    IReadOnlyCollection<string> MatchKeys,
    string InboundEventId,
    IReadOnlyDictionary<string, JsonElement> Payload,
    DateTime ReceivedAt)
{
    public string? CorrelationKey { get; init; }
}
