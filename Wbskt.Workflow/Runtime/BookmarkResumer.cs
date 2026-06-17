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
        
        /* [RJ] For my information. (yes this is AI explanation. but this is correct)
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
            _logger?.LogInformation("Event {EventId} dropped as idempotent (lost idempotency claim race)", evt.InboundEventId);
            return new BookmarkMatchResult(false, null, true);
        }

        _logger?.LogDebug("Event {EventId} acquired idempotency claim", evt.InboundEventId);

        var bookmarks = await _bookmarkProvider.GetAllByMatchKeysAsync(evt.MatchKeys, ct);
        if (bookmarks.Count == 0)
        {
            _logger?.LogDebug("No matching bookmark found for event {EventId} using match keys", evt.InboundEventId);
            return new BookmarkMatchResult(false, null, false, claim.KeyValue);
        }

        foreach (var bookmark in bookmarks)
        {
            try
            {
                _logger?.LogInformation("Event {EventId} matched bookmark {BookmarkId} for run {RunId}", evt.InboundEventId, bookmark.Id, bookmark.RunId);

                await _bookmarkProvider.DeleteAsync(bookmark.RefId, ct);
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

                await _runDispatcher.DispatchAsync(new BranchExecutionRequest(bookmark.RunId, branch.Id, BranchExecutionReason.BookmarkResumed), ct);
                await _bookmarkProvider.DeleteSiblingsAsync(bookmark.RunId, branch.Id, bookmark.Id, ct);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to resume bookmark for event {EventId}", evt.InboundEventId);
                throw;
            }
        }

        // We mark the idempotency key as succeeded now that all resumes have been dispatched
        await _idempotencyKeyProvider.MarkSucceededAsync(claim.KeyValue, "{}", ct);

        return new BookmarkMatchResult(true, bookmarks.First().Id, false);
    }

    
    // [RJ]: TODO: remove this. remnant of old plan
    /*
     * Here is why:

       If you look at the V3 Design Document (Section 3.4.2), there is a pseudo-code function defined as  function ResumeViaBookmark(bookmark, payload, takingPort) . The idea was to have a single, unified method that would do the actual database work of
       deleting the bookmark, updating the branch state, and dispatching the run to the  IRunDispatcher .

       However, during implementation, the workflow engine ended up with two distinct "resume" paths that required slightly different behavior, and the resume logic got inlined into both of them rather than calling a shared method:

       1. Signal-driven resumes ( BookmarkResumer.MatchInboundAsync ): When an HTTP or MQTT event wakes a bookmark, it needs to merge the  evt.Payload  into the branch's local state so the workflow has access to the event data. It handles all the DB
       operations and dispatching directly.
       2. Timer-driven resumes ( BookmarkScheduler.ProcessDueBookmarksAsync ): When a TTL/companion timer expires, a background service leases the due bookmark from the database. It needs to set the  PendingTakePort  to the  TtlPort  (so the workflow skips
       execution and jumps straight to the timeout edge), and it also does all the DB deletion and dispatching inline.

       Because both paths had specialized logic (Payload merging vs. TTL port overriding), the developers just inlined the core "delete bookmark -> update branch -> dispatch" operations into those two methods.  ResumeViaBookmarkAsync  was left behind in
       the interface (and is only used in a couple of unit tests that invoke it manually) but it serves no purpose in the actual engine anymore.
     */
    public async Task ResumeViaBookmarkAsync(long bookmarkId, IReadOnlyDictionary<string, JsonElement> wakePayload, CancellationToken ct)
    {
        BookmarkRow? bookmark = await _bookmarkProvider.GetByIdAsync(bookmarkId, ct);
        if (bookmark is null)
        {
            return;
        }

        await _bookmarkProvider.DeleteAsync(bookmark.RefId, ct);
        var branch = await _branchProvider.GetByRefIdAsync(bookmark.BranchRefId, ct);

        if (wakePayload.Count > 0)
        {
            string mergedLocalJson = MergeWakePayload(branch.LocalJson, wakePayload);
            await _branchProvider.UpdatePointerAsync(branch.Id, branch.NodeId, branch.Status, mergedLocalJson, branch.LastOutputJson, ct);
        }

        await _runDispatcher.DispatchAsync(new BranchExecutionRequest(bookmark.RunId, branch.Id, BranchExecutionReason.BookmarkResumed), ct);
        await _bookmarkProvider.DeleteSiblingsAsync(bookmark.RunId, branch.Id, bookmark.Id, ct);
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
