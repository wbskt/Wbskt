using Wbskt.Management.Host.Models;
using Wbskt.Workflow.Abstraction.Integrations;

namespace Wbskt.Management.Host.Providers;

public interface IIntegrationProvider
{
    Task<IReadOnlyCollection<Integration>> GetAllAsync(int workspaceId, IntegrationType? type, CancellationToken ct);

    Task<Integration?> FindByRefIdAsync(Guid refId, CancellationToken ct);

    /// <summary>Throws <see cref="Microsoft.Data.SqlClient.SqlException"/> 50031 at the workspace limit, and 2627 on a taken name.</summary>
    Task InsertAsync(Integration integration, int maxPerWorkspace, CancellationToken ct);

    /// <summary>
    /// Saves a rename or new settings; <paramref name="ciphertext"/> null keeps the sealed secret.
    /// Throws <see cref="Microsoft.Data.SqlClient.SqlException"/> 2627 on a taken name.
    /// </summary>
    Task UpdateAsync(int workspaceId, int id, string name, string configJson, bool configChanged, byte[]? ciphertext, byte? keyId, string? secretHint, bool secretRotated, CancellationToken ct);

    /// <summary>Records a test of the saved integration, unless it changed since <paramref name="expectedUpdatedAt"/>. True when recorded.</summary>
    Task<bool> RecordTestAsync(int id, DateTime expectedUpdatedAt, bool ok, string? code, string? error, string? metadata, CancellationToken ct);

    Task<IReadOnlyCollection<IntegrationUse>> GetUsageAsync(int id, CancellationToken ct);

    /// <summary>Deletes the integration unless a published current version uses it; returns those uses (empty when deleted).</summary>
    Task<IReadOnlyCollection<IntegrationUse>> DeleteAsync(int workspaceId, int id, CancellationToken ct);
}
