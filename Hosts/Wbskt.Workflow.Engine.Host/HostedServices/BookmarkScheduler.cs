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
    private readonly TimeSpan _orphanGcInterval;

    public BookmarkScheduler(
        IClock clock,
        IHostIdentity hostIdentity,
        IBranchProvider branchProvider,
        IBookmarkProvider bookmarkProvider,
        IRunDispatcher runDispatcher,
        ILogger<BookmarkScheduler> logger,
        TimeSpan? pollInterval = null,
        TimeSpan? orphanGcInterval = null)
    {
        _clock = clock;
        _hostIdentity = hostIdentity;
        _branchProvider = branchProvider;
        _bookmarkProvider = bookmarkProvider;
        _runDispatcher = runDispatcher;
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
        _orphanGcInterval = orphanGcInterval ?? TimeSpan.FromMinutes(5);
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

    public Task RunOrphanGcAsync(CancellationToken ct)
    {
        return _bookmarkProvider.DeleteOrphansAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ExecuteGuardedAsync(ProcessDueBookmarksAsync, "Bookmark scheduler tick failed.", stoppingToken);

        using var pollTimer = new PeriodicTimer(_pollInterval);
        using var orphanGcTimer = new PeriodicTimer(_orphanGcInterval);

        Task<bool> nextPoll = pollTimer.WaitForNextTickAsync(stoppingToken).AsTask();
        Task<bool> nextOrphanGc = orphanGcTimer.WaitForNextTickAsync(stoppingToken).AsTask();

        while (!stoppingToken.IsCancellationRequested)
        {
            Task<bool> completed;

            try
            {
                completed = await Task.WhenAny(nextPoll, nextOrphanGc);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            if (completed == nextPoll)
            {
                if (!await nextPoll)
                {
                    return;
                }

                await ExecuteGuardedAsync(ProcessDueBookmarksAsync, "Bookmark scheduler tick failed.", stoppingToken);
                nextPoll = pollTimer.WaitForNextTickAsync(stoppingToken).AsTask();
                continue;
            }

            if (!await nextOrphanGc)
            {
                return;
            }

            await ExecuteGuardedAsync(RunOrphanGcAsync, "Bookmark orphan GC failed.", stoppingToken);
            nextOrphanGc = orphanGcTimer.WaitForNextTickAsync(stoppingToken).AsTask();
        }
    }

    private async Task ExecuteGuardedAsync(Func<CancellationToken, Task> operation, string errorMessage, CancellationToken stoppingToken)
    {
        try
        {
            await operation(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, errorMessage);
        }
    }
}
