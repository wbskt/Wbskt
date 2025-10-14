using Wbskt.Common.Enums;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers;

public interface IWorkflowsReader
{
    Task<List<WorkflowRecord>> GetAllForUserAsync(int userId, CancellationToken cancellationToken);
    Task<WorkflowRecord?> GetByRefIdAsync(Guid refId, CancellationToken cancellationToken);
    Task<List<WorkflowRecord>> GetActiveWorkflowsByTriggerTypeAsync(TriggerType triggerType, CancellationToken cancellationToken);
    Task<WorkflowRecord?> GetByWebhookIdAsync(Guid webhookId, CancellationToken cancellationToken);
}
