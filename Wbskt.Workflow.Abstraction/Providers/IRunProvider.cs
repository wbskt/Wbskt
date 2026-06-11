using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

public interface IRunProvider
{
    Task<RunRow> CreateAsync(RunRow row, CancellationToken ct);
    Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct);
    Task<RunRow> GetByIdAsync(long runId, CancellationToken ct);
    Task<RunRow> GetByRefIdAsync(Guid refId, CancellationToken ct);
    Task<IReadOnlyCollection<RunRow>> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct);
    Task<IReadOnlyCollection<RunRow>> GetActiveByWorkflowRefIdCorrelationKeyAsync(Guid workflowRefId, string correlationKey, CancellationToken ct);
    Task<IReadOnlyCollection<RunRow>> GetActiveByCorrelationAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct)
        => Task.FromResult<IReadOnlyCollection<RunRow>>(System.Array.Empty<RunRow>());
    Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct);
    Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct);
    Task<long> CountByStatusAsync(string status, CancellationToken ct);
    Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, CancellationToken ct);
    Task<RunRow> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct);
}
