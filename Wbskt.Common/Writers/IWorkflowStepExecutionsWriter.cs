using Wbskt.Common.Records;

namespace Wbskt.Common.Writers;

public interface IWorkflowStepExecutionsWriter
{
    Task<int> CreateAsync(WorkflowStepExecutionRecord record, CancellationToken cancellationToken);
    Task UpdateAsync(WorkflowStepExecutionRecord record, CancellationToken cancellationToken);
}
