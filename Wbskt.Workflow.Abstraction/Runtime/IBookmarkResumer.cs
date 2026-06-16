using System.Text.Json;

namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IBookmarkResumer
{
    Task<BookmarkMatchResult> MatchInboundAsync(InboundEvent evt, CancellationToken ct);
    Task ResumeViaBookmarkAsync(long bookmarkId, IReadOnlyDictionary<string, JsonElement> wakePayload, CancellationToken ct);
}

public sealed record BookmarkMatchResult(bool Matched, long? BookmarkId, bool Idempotent, string? ClaimKey = null);
