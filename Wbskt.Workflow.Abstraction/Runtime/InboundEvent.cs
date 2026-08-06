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

    /// <summary>
    /// The shared secret the caller presented, for channels that have one (today: webhook). Deliberately
    /// kept out of <see cref="Payload"/> - the payload is persisted as the run's trigger data and
    /// rendered in the history trace, and a credential has no business in either.
    /// </summary>
    public string? Secret { get; init; }
}
