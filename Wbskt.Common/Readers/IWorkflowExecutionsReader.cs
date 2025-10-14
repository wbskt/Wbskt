using Wbskt.Common.Records;

namespace Wbskt.Common.Readers;

public interface IWorkflowExecutionsReader
{
    Task<List<WorkflowExecutionRecord>> GetAllForWorkflowAsync(Guid workflowRefId, CancellationToken cancellationToken);
    Task<WorkflowExecutionRecord?> GetByIdAsync(int executionId, CancellationToken cancellationToken);
}
