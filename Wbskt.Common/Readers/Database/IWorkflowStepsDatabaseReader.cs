using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database;

public interface IWorkflowStepsDatabaseReader
{
    Task<List<WorkflowStepRecord>> GetAllForWorkflowAsync(int workflowId, CancellationToken cancellationToken);
}
