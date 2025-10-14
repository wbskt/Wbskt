using Wbskt.Common.Records;

namespace Wbskt.Common.Readers;

public interface IWorkflowStepExecutionsReader
{
    Task<List<WorkflowStepExecutionRecord>> GetAllForExecutionAsync(int executionId, CancellationToken cancellationToken);
}
