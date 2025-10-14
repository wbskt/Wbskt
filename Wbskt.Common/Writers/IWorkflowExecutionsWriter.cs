using Wbskt.Common.Records;

namespace Wbskt.Common.Writers;

public interface IWorkflowExecutionsWriter
{
    Task<int> CreateAsync(WorkflowExecutionRecord execution, CancellationToken cancellationToken);
    Task UpdateAsync(WorkflowExecutionRecord execution, CancellationToken cancellationToken);
}
