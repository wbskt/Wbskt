namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IBookmarkResumer
{
    /// <summary>
    /// Resumes every bookmark the event matches, under an idempotency claim on the event's id.
    /// </summary>
    /// <param name="hasRegistrations">
    /// Whether any trigger registration matches the event. When it does not and no bookmark matches
    /// either, nothing will act on the event, so no claim is written: most device messages match no
    /// workflow, and claiming each one cost an insert and an update for nothing.
    /// </param>
    Task<BookmarkMatchResult> MatchInboundAsync(InboundEvent evt, bool hasRegistrations, CancellationToken ct);
}

public sealed record BookmarkMatchResult(bool Matched, long? BookmarkId, bool Idempotent, string? ClaimKey = null);
