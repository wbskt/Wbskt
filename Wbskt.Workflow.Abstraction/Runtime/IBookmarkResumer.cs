using System.Text.Json;

namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IBookmarkResumer
{
    Task<BookmarkMatchResult> MatchInboundAsync(InboundEvent evt, CancellationToken ct);
}

public sealed record InboundEvent(
    string ChannelKind,
    string CorrelationKey,
    string InboundEventId,
    IReadOnlyDictionary<string, JsonElement> Payload,
    DateTime ReceivedAt);

public sealed record BookmarkMatchResult(bool Matched, long? BookmarkId, bool Idempotent);
