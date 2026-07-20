using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Microsoft.Extensions.Logging;

namespace Wbskt.Workflow.Runtime;

internal sealed class BookmarkResumer : IBookmarkResumer
{
    private readonly IBookmarkProvider _bookmarkProvider;
    private readonly IIdempotencyKeyProvider _idempotencyKeyProvider;
    private readonly IBranchProvider _branchProvider;
    private readonly IRunDispatcher _runDispatcher;
    private readonly ILogger<BookmarkResumer>? _logger;

    public BookmarkResumer(
        IBookmarkProvider bookmarkProvider,
        IIdempotencyKeyProvider idempotencyKeyProvider,
        IBranchProvider branchProvider,
        IRunDispatcher runDispatcher,
        ILogger<BookmarkResumer>? logger = null)
    {
        _bookmarkProvider = bookmarkProvider;
        _idempotencyKeyProvider = idempotencyKeyProvider;
        _branchProvider = branchProvider;
        _runDispatcher = runDispatcher;
        _logger = logger;
    }

    public async Task<BookmarkMatchResult> MatchInboundAsync(InboundEvent evt, CancellationToken ct)
    {
        string idempotencyKey = $"inbound-event:{evt.InboundEventId}";
        Guid claimToken = Guid.NewGuid();
        
        /* [RJ]: For my information. (yes this is AI explanation. but this is correct)
        Deciphering Inbound Idempotency
          
          I've realized the random GUID is a lock or claim token for inbound events, used before knowing the target branch(es). The `IdempotencyKeys` table's `branchRefId` column, originally for outbound executions, is repurposed here. Since inbound events may
          resume multiple branches or spawn new runs via `TriggerDispatcher`, a random GUID acts as a placeholder for concurrency control. This allows for deduplication without prior branch knowledge.
          No, it's not a typo—it's actually a clever (though slightly hacky) way to use that column as a distributed lock token!

          Here's why it does that:

          The  IdempotencyKeys  table was originally designed to deduplicate outbound side-effects (e.g., executing a  SendEmail  node). In those cases, the engine knows exactly which  RunId ,  BranchRefId , and  NodeId  is executing, so it fills those
          columns out accurately.

          However, for inbound events, the engine needs to deduplicate the event before it even knows what it's going to do. At the moment  MatchInboundAsync  runs:

          • We don't know if the event will resume a bookmark.
          • We don't know if it will resume multiple bookmarks.
          • We don't know if it will start a brand new run via the  TriggerDispatcher .

          Because there is no "real" branch yet, the code mints a  Guid.NewGuid()  and passes it into the  branchRefId  parameter. It acts purely as a claim token for a race condition:

          1. Server A and Server B both receive the exact same webhook delivery at the same millisecond.
          2. Server A mints  Guid-A  and calls  UpsertPendingAsync .
          3. Server B mints  Guid-B  and calls  UpsertPendingAsync .
          4. The database enforces a  HOLDLOCK  in the stored procedure. Server A's insert succeeds, storing  Guid-A  in the  BranchRefId  column.
          5. Server B's insert is blocked, and then it simply reads the existing row.
          6. The database returns the row to both servers.
          7. Server A checks  if (claim.BranchRefId != claimToken) . It matches  Guid-A , so Server A proceeds to process the event.
          8. Server B checks  if (claim.BranchRefId != claimToken) . It sees  Guid-A  instead of its own  Guid-B , knows it lost the race, and safely drops the event ( Idempotent = true ).

          So while the parameter is named  branchRefId , in the context of inbound events it is just being used as a unique lock identifier for the current thread/process!
        */
        IdempotencyKeyRow claim = await _idempotencyKeyProvider.UpsertPendingAsync(idempotencyKey, 0, claimToken, Guid.Empty, 0, ct);
        if (claim.BranchRefId != claimToken)
        {
            if (string.Equals(claim.Status, "Failed", StringComparison.Ordinal))
            {
                // The previous claimant crashed or threw mid-resume without ever delivering the
                // event (see the catch below), leaving a wedged 'Failed' claim. Reclaim it instead
                // of permanently dropping every redelivery of this event as a duplicate.
                claim = await _idempotencyKeyProvider.ReclaimFailedAsync(idempotencyKey, claimToken, ct);
            }

            if (claim.BranchRefId != claimToken)
            {
                _logger?.LogInformation("Event {EventId} dropped as idempotent (lost idempotency claim race)", evt.InboundEventId);
                return new BookmarkMatchResult(false, null, true);
            }

            _logger?.LogInformation("Event {EventId} reclaimed a stale Failed idempotency claim", evt.InboundEventId);
        }

        _logger?.LogDebug("Event {EventId} acquired idempotency claim", evt.InboundEventId);

        try
        {
            var bookmarks = await _bookmarkProvider.GetAllByMatchKeysAsync(evt.MatchKeys, ct);
            if (bookmarks.Count == 0)
            {
                _logger?.LogDebug("No matching bookmark found for event {EventId} using match keys", evt.InboundEventId);
                return new BookmarkMatchResult(false, null, false, claim.KeyValue);
            }

            foreach (var bookmark in bookmarks)
            {
                // Single-row model: claim (delete) the bookmark before touching anything else. If a
                // concurrent TTL expiry already claimed it, we lost the race and must not resume it.
                if (!await _bookmarkProvider.TryClaimAsync(bookmark.RefId, ct))
                {
                    _logger?.LogDebug("Bookmark {BookmarkId} for event {EventId} already claimed (lost race with TTL/another matcher)", bookmark.Id, evt.InboundEventId);
                    continue;
                }

                _logger?.LogInformation("Event {EventId} matched bookmark {BookmarkId} for run {RunId}", evt.InboundEventId, bookmark.Id, bookmark.RunId);

                var branch = await _branchProvider.GetByRefIdAsync(bookmark.BranchRefId, ct);

                // Deliver the wake payload to the resumed branch under the reserved "__wake" key so the
                // parked node (and downstream nodes) can read what woke them. The branch keeps its pointer
                // and Waiting status; the re-executed node flips it to Active when it continues.
                if (evt.Payload.Count > 0)
                {
                    _logger?.LogDebug("Merging wake payload into branch {BranchId} local state", branch.Id);
                    string mergedLocalJson = MergeWakePayload(branch.LocalJson, evt.Payload);
                    await _branchProvider.UpdatePointerAsync(branch.Id, branch.NodeId, branch.Status, mergedLocalJson, branch.LastOutputJson, ct);
                }

                // Dispatch is the last step - the branch is only resumed once the bookmark is
                // irrevocably claimed and any wake payload is durably merged.
                await _runDispatcher.DispatchAsync(new BranchExecutionRequest(bookmark.RunId, branch.Id, BranchExecutionReason.BookmarkResumed), ct);
            }

            // We mark the idempotency key as succeeded now that all resumes have been dispatched
            await _idempotencyKeyProvider.MarkSucceededAsync(claim.KeyValue, "{}", ct);

            return new BookmarkMatchResult(true, bookmarks.First().Id, false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to resume bookmark for event {EventId}", evt.InboundEventId);
            // Release the claim so a redelivery of this event (or the next PendingTriggerEventDrainer
            // pass) isn't permanently dropped as a duplicate of a resume that never actually happened.
            string errorJson = JsonSerializer.Serialize(new { ex.Message }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await _idempotencyKeyProvider.MarkFailedAsync(claim.KeyValue, errorJson, ct);
            throw;
        }
    }

    private static string MergeWakePayload(string localJson, IReadOnlyDictionary<string, JsonElement> wakePayload)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Dictionary<string, JsonElement> local = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(localJson, options)
            ?? new Dictionary<string, JsonElement>();
        local["__wake"] = JsonSerializer.SerializeToElement(wakePayload, options);
        return JsonSerializer.Serialize(local, options);
    }
}
