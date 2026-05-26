using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

public interface IIdempotencyKeyProvider
{
    Task<IdempotencyKeyRow> UpsertPendingAsync(string keyValue, int runId, Guid branchRefId, Guid nodeId, int attempt, CancellationToken ct);
    Task<IdempotencyKeyRow> GetByKeyAsync(string keyValue, CancellationToken ct);
    Task<IdempotencyKeyRow> MarkSucceededAsync(string keyValue, string resultJson, CancellationToken ct);
    Task<IdempotencyKeyRow> MarkFailedAsync(string keyValue, string errorJson, CancellationToken ct);
}
