using Wbskt.Common.Readers.Database;
using Wbskt.Common.Records;
using Wbskt.Common.Services;

namespace Wbskt.Common.Readers.Cache;

internal sealed class CachedWorkflowStepsReader : IWorkflowStepsReader
{
    private readonly IWorkflowStepsDatabaseReader _databaseReader;
    private readonly ICacheService _cacheService;

    public CachedWorkflowStepsReader(IWorkflowStepsDatabaseReader databaseReader, ICacheService cacheService)
    {
        _databaseReader = databaseReader;
        _cacheService = cacheService;
    }

    public Task<List<WorkflowStepRecord>> GetAllForWorkflowAsync(int workflowId, CancellationToken cancellationToken)
    {
        var cacheKey = $"WorkflowSteps_{workflowId}";
        return _cacheService.GetOrSetAsync(cacheKey, () => _databaseReader.GetAllForWorkflowAsync(workflowId, cancellationToken), TimeSpan.FromMinutes(5), cancellationToken);
    }
}
