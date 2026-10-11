using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

/// <summary>Deletes bookmarks whose run or branch no longer exists.</summary>
public sealed class BookmarkOrphanGc(
    ILeadershipGate leadership,
    IServiceScopeFactory scopeFactory,
    ILogger<BookmarkOrphanGc> logger,
    IOptions<WorkflowEngineOptions> options) : LeaderPeriodicWorker(leadership, scopeFactory, logger)
{
    protected override string Name => "Bookmark orphan GC";

    protected override TimeSpan Interval { get; } = options.Value.BookmarkOrphanGcInterval;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var bookmarkProvider = services.GetRequiredService<IBookmarkProvider>();
        int deletedCount = await bookmarkProvider.DeleteOrphansAsync(ct);
        if (deletedCount > 0)
        {
            Logger.LogInformation("Orphan GC removed {Count} orphaned bookmarks", deletedCount);
        }
    }
}
