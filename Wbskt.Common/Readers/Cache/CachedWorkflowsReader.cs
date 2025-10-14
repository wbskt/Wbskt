using Wbskt.Common.Readers.Database;
using Wbskt.Common.Records;
using Wbskt.Common.Services;

namespace Wbskt.Common.Readers.Cache;

internal sealed class CachedWorkflowsReader : IWorkflowsReader
{
    private readonly IWorkflowsDatabaseReader _databaseReader;
    private readonly ICacheService _cacheService;

    public CachedWorkflowsReader(IWorkflowsDatabaseReader databaseReader, ICacheService cacheService)
    {
        _databaseReader = databaseReader;
        _cacheService = cacheService;
    }

    public Task<List<WorkflowRecord>> GetAllForUserAsync(int userId, CancellationToken cancellationToken)
    {
        var cacheKey = $"Workflows_User_{userId}";
        return _cacheService.GetOrSetAsync(cacheKey, () => _databaseReader.GetAllForUserAsync(userId, cancellationToken), TimeSpan.FromMinutes(5), cancellationToken);
    }

    public async Task<WorkflowRecord?> GetByRefIdAsync(Guid refId, CancellationToken cancellationToken)
    {
        // This is not ideal, but for now we will rely on the user-specific cache.
        // In the future, we can implement a more granular cache for individual workflows.
        var workflows = await _cacheService.GetOrSetAsync("AllWorkflows", () => _databaseReader.GetAllForUserAsync(0, cancellationToken), TimeSpan.FromMinutes(5), cancellationToken);
        return workflows.FirstOrDefault(p => p.RefId == refId);
    }

    public Task<WorkflowRecord?> GetByWebhookIdAsync(Guid webhookId, CancellationToken cancellationToken)
    {
        // This lookup is not cached and passes directly to the database reader.
        return _databaseReader.GetByWebhookIdAsync(webhookId, cancellationToken);
    }

    public Task<List<WorkflowRecord>> GetActiveWorkflowsByTriggerTypeAsync(string triggerType, CancellationToken cancellationToken)
    {
        // This should not be cached as it is a frequent query for the scheduler.
        return _databaseReader.GetActiveWorkflowsByTriggerTypeAsync(triggerType, cancellationToken);
    }
}
