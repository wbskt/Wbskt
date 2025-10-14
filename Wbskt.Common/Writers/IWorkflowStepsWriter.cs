using Wbskt.Common.Records;

namespace Wbskt.Common.Writers;

public interface IWorkflowStepsWriter
{
    Task BulkUpdateForWorkflowAsync(int workflowId, IEnumerable<WorkflowStepRecord> steps, CancellationToken cancellationToken);
}
