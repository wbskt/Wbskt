using Wbskt.Common.Records;

namespace Wbskt.Common.Readers;

public interface IWorkflowStepsReader
{
    Task<List<WorkflowStepRecord>> GetAllForWorkflowAsync(int workflowId, CancellationToken cancellationToken);
}
