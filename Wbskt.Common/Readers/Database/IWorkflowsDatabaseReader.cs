using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database;

public interface IWorkflowsDatabaseReader
{
    Task<List<WorkflowRecord>> GetAllForUserAsync(int userId, CancellationToken cancellationToken);
    Task<WorkflowRecord?> GetByRefIdAsync(Guid refId, CancellationToken cancellationToken);
}
