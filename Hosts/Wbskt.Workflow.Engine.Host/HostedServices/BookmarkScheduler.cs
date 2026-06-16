using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

public sealed class BookmarkScheduler : BackgroundService
{
    private const int BatchSize = 64;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private readonly IClock _clock;
    private readonly IHostIdentity _hostIdentity;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRunDispatcher _runDispatcher;
    private readonly ILogger<BookmarkScheduler> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _orphanGcInterval;
    private readonly int _batchSize;
    private readonly TimeSpan _leaseDuration;

    [ActivatorUtilitiesConstructor]
    public BookmarkScheduler(
        IClock clock,
        IHostIdentity hostIdentity,
        IServiceScopeFactory scopeFactory,
        IRunDispatcher runDispatcher,
        ILogger<BookmarkScheduler> logger,
        IOptions<WorkflowEngineOptions> options)
        : this(
            clock,
            hostIdentity,
            scopeFactory,
            runDispatcher,
            logger,
            options.Value.BookmarkPollInterval,
            options.Value.BookmarkOrphanGcInterval,
            options.Value.BookmarkLeaseBatchSize,
            TimeSpan.FromSeconds(options.Value.LeaseDurationSeconds))
    {
    }

    internal BookmarkScheduler(
        IClock clock,
        IHostIdentity hostIdentity,
        IServiceScopeFactory scopeFactory,
        IRunDispatcher runDispatcher,
        ILogger<BookmarkScheduler> logger,
        TimeSpan? pollInterval = null,
        TimeSpan? orphanGcInterval = null,
        int batchSize = 64,
        TimeSpan? leaseDuration = null)
    {
        _clock = clock;
        _hostIdentity = hostIdentity;
        _scopeFactory = scopeFactory;
        _runDispatcher = runDispatcher;
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
        _orphanGcInterval = orphanGcInterval ?? TimeSpan.FromMinutes(5);
        _batchSize = batchSize;
        _leaseDuration = leaseDuration ?? TimeSpan.FromMinutes(2);
    }

    internal BookmarkScheduler(
        IClock clock,
        IHostIdentity hostIdentity,
        IBranchProvider branchProvider,
        IBookmarkProvider bookmarkProvider,
        IRunDispatcher runDispatcher,
        ILogger<BookmarkScheduler> logger,
        TimeSpan? pollInterval = null,
        TimeSpan? orphanGcInterval = null,
        int batchSize = 64,
        TimeSpan? leaseDuration = null)
        : this(
            clock,
            hostIdentity,
            new StaticScopeFactory(branchProvider, bookmarkProvider),
            runDispatcher,
            logger,
            pollInterval,
            orphanGcInterval,
            batchSize,
            leaseDuration)
    {
    }

    public async Task ProcessDueBookmarksAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        var branchProvider = scope.ServiceProvider.GetRequiredService<IBranchProvider>();
        var bookmarkProvider = scope.ServiceProvider.GetRequiredService<IBookmarkProvider>();
        IReadOnlyCollection<Abstraction.Entities.BookmarkRow> leasedBookmarks =
            await bookmarkProvider.LeaseDueAsync(_clock.UtcNow, _batchSize, _hostIdentity.HostId, _leaseDuration, ct);

        foreach (var bookmark in leasedBookmarks)
        {
            var branch = await branchProvider.GetByRefIdAsync(bookmark.BranchRefId, ct);
            if (!string.IsNullOrEmpty(bookmark.TtlPort))
            {
                branch = branch with { PendingTakePort = bookmark.TtlPort };
                await branchProvider.UpsertAsync(branch, ct);
            }
            await _runDispatcher.DispatchAsync(new BranchExecutionRequest(bookmark.RunId, branch.Id, BranchExecutionReason.BookmarkResumed), ct);
            await bookmarkProvider.DeleteAsync(bookmark.RefId, ct);
            await bookmarkProvider.DeleteSiblingsAsync(bookmark.RunId, branch.Id, bookmark.Id, ct);
        }
    }

    public async Task RunOrphanGcAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        var bookmarkProvider = scope.ServiceProvider.GetRequiredService<IBookmarkProvider>();
        await bookmarkProvider.DeleteOrphansAsync(ct);
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

    private sealed class StaticScopeFactory(IBranchProvider branchProvider, IBookmarkProvider bookmarkProvider) : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            return new StaticServiceScope(branchProvider, bookmarkProvider);
        }
    }

    private sealed class StaticServiceScope(IBranchProvider branchProvider, IBookmarkProvider bookmarkProvider) : IServiceScope, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider { get; } = new StaticServiceProvider(branchProvider, bookmarkProvider);

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StaticServiceProvider(IBranchProvider branchProvider, IBookmarkProvider bookmarkProvider) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IBranchProvider))
            {
                return branchProvider;
            }

            if (serviceType == typeof(IBookmarkProvider))
            {
                return bookmarkProvider;
            }

            return null;
        }
    }
}
