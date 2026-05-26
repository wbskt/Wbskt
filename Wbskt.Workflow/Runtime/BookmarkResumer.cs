using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

public sealed class BookmarkResumer : IBookmarkResumer
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
        string idempotencyKey = $"{evt.ChannelKind}:{evt.CorrelationKey}:{evt.InboundEventId}";
        Guid claimToken = Guid.NewGuid();
        IdempotencyKeyRow claim = await _idempotencyKeyProvider.UpsertPendingAsync(idempotencyKey, 0, claimToken, Guid.Empty, 0, ct);
        if (claim.BranchRefId != claimToken)
        {
            return new BookmarkMatchResult(false, null, true);
        }

        string matchKey = $"{evt.ChannelKind}:{evt.CorrelationKey}";
        BookmarkRow? bookmark = (await _bookmarkProvider.GetAllByMatchKeyAsync(matchKey, ct)).FirstOrDefault();
        if (bookmark is null)
        {
            return new BookmarkMatchResult(false, null, false);
        }

        await _bookmarkProvider.DeleteAsync(bookmark.RefId, ct);
        var branch = await _branchProvider.GetByRefIdAsync(bookmark.BranchRefId, ct);
        await _runDispatcher.DispatchAsync(new BranchExecutionRequest(bookmark.RunId, branch.Id, BranchExecutionReason.BookmarkResumed), ct);
        await _bookmarkProvider.DeleteSiblingsAsync(bookmark.RunId, branch.Id, bookmark.Id, ct);
        return new BookmarkMatchResult(true, bookmark.Id, false);
    }

    public async Task ResumeViaBookmarkAsync(long bookmarkId, IReadOnlyDictionary<string, JsonElement> wakePayload, CancellationToken ct)
    {
        _ = wakePayload;

        BookmarkRow? bookmark = await _bookmarkProvider.GetByIdAsync(bookmarkId, ct);
        if (bookmark is null)
        {
            return;
        }

        await _bookmarkProvider.DeleteAsync(bookmark.RefId, ct);
        var branch = await _branchProvider.GetByRefIdAsync(bookmark.BranchRefId, ct);
        await _runDispatcher.DispatchAsync(new BranchExecutionRequest(bookmark.RunId, branch.Id, BranchExecutionReason.BookmarkResumed), ct);
        await _bookmarkProvider.DeleteSiblingsAsync(bookmark.RunId, branch.Id, bookmark.Id, ct);
    }
}
