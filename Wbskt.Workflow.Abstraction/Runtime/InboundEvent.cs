using System.Text.Json;

namespace Wbskt.Workflow.Abstraction.Runtime;

public sealed record InboundEvent(
    string ChannelKind,
    string CorrelationKey,
    string InboundEventId,
    IReadOnlyDictionary<string, JsonElement> Payload,
    DateTime ReceivedAt);
