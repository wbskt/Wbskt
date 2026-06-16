using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class BookmarkResumer : IBookmarkResumer
{
    private readonly IBookmarkProvider _bookmarkProvider;
    private readonly IIdempotencyKeyProvider _idempotencyKeyProvider;
    private readonly IBranchProvider _branchProvider;
    private readonly IRunDispatcher _runDispatcher;

    public BookmarkResumer(
        IBookmarkProvider bookmarkProvider,
        IIdempotencyKeyProvider idempotencyKeyProvider,
        IBranchProvider branchProvider,
        IRunDispatcher runDispatcher)
    {
        _bookmarkProvider = bookmarkProvider;
        _idempotencyKeyProvider = idempotencyKeyProvider;
        _branchProvider = branchProvider;
        _runDispatcher = runDispatcher;
    }

    public async Task<BookmarkMatchResult> MatchInboundAsync(InboundEvent evt, CancellationToken ct)
    {
        string idempotencyKey = $"inbound-event:{evt.InboundEventId}";
        Guid claimToken = Guid.NewGuid();
        IdempotencyKeyRow claim = await _idempotencyKeyProvider.UpsertPendingAsync(idempotencyKey, 0, claimToken, Guid.Empty, 0, ct);
        if (claim.BranchRefId != claimToken)
        {
            return new BookmarkMatchResult(false, null, true);
        }

        var bookmarks = await _bookmarkProvider.GetAllByMatchKeysAsync(evt.MatchKeys, ct);
        if (bookmarks.Count == 0)
        {
            return new BookmarkMatchResult(false, null, false, claim.KeyValue);
        }

        foreach (var bookmark in bookmarks)
        {
            await _bookmarkProvider.DeleteAsync(bookmark.RefId, ct);
            var branch = await _branchProvider.GetByRefIdAsync(bookmark.BranchRefId, ct);

            // Deliver the wake payload to the resumed branch under the reserved "__wake" key so the
            // parked node (and downstream nodes) can read what woke them. The branch keeps its pointer
            // and Waiting status; the re-executed node flips it to Active when it continues.
            if (evt.Payload.Count > 0)
            {
                string mergedLocalJson = MergeWakePayload(branch.LocalJson, evt.Payload);
                await _branchProvider.UpdatePointerAsync(branch.Id, branch.NodeId, branch.Status, mergedLocalJson, branch.LastOutputJson, ct);
            }

            await _runDispatcher.DispatchAsync(new BranchExecutionRequest(bookmark.RunId, branch.Id, BranchExecutionReason.BookmarkResumed), ct);
            await _bookmarkProvider.DeleteSiblingsAsync(bookmark.RunId, branch.Id, bookmark.Id, ct);
        }

        // We mark the idempotency key as succeeded now that all resumes have been dispatched
        await _idempotencyKeyProvider.MarkSucceededAsync(claim.KeyValue, "{}", ct);

        return new BookmarkMatchResult(true, bookmarks.First().Id, false);
    }

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
