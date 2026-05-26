using Microsoft.Extensions.Hosting;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

public sealed class BookmarkScheduler : BackgroundService
{
    private const int BatchSize = 64;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private readonly IClock _clock;
    private readonly IHostIdentity _hostIdentity;
    private readonly IBranchProvider _branchProvider;
    private readonly IBookmarkProvider _bookmarkProvider;
    private readonly IRunDispatcher _runDispatcher;
    private readonly ILogger<BookmarkScheduler> _logger;
    private readonly TimeSpan _pollInterval;

    public BookmarkScheduler(
        IClock clock,
        IHostIdentity hostIdentity,
        IBranchProvider branchProvider,
        IBookmarkProvider bookmarkProvider,
        IRunDispatcher runDispatcher,
        ILogger<BookmarkScheduler> logger,
        TimeSpan? pollInterval = null)
    {
        _clock = clock;
        _hostIdentity = hostIdentity;
        _branchProvider = branchProvider;
        _bookmarkProvider = bookmarkProvider;
        _runDispatcher = runDispatcher;
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
    }

    public async Task ProcessDueBookmarksAsync(CancellationToken ct)
    {
        IReadOnlyCollection<Wbskt.Workflow.Abstraction.Entities.BookmarkRow> leasedBookmarks =
            await _bookmarkProvider.LeaseDueAsync(_clock.UtcNow, BatchSize, _hostIdentity.HostId, LeaseDuration, ct);

        foreach (var bookmark in leasedBookmarks)
        {
            var branch = await _branchProvider.GetByRefIdAsync(bookmark.BranchRefId, ct);
            await _runDispatcher.DispatchAsync(new BranchExecutionRequest(bookmark.RunId, branch.Id, BranchExecutionReason.BookmarkResumed), ct);
            await _bookmarkProvider.DeleteAsync(bookmark.RefId, ct);
            await _bookmarkProvider.DeleteSiblingsAsync(bookmark.RunId, branch.Id, bookmark.Id, ct);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_pollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueBookmarksAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bookmark scheduler tick failed.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
