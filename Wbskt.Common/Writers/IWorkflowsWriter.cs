using Wbskt.Common.Records;

namespace Wbskt.Common.Writers;

public interface IWorkflowsWriter
{
    Task<int> CreateAsync(WorkflowRecord workflow, CancellationToken cancellationToken);
    Task UpdateAsync(WorkflowRecord workflow, CancellationToken cancellationToken);
    Task DeleteAsync(Guid refId, CancellationToken cancellationToken);
}
