using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Engine.Contracts;

namespace Wbskt.Workflow.Integrations;

/// <summary>An integration as the engine reads it to send: sealed, with no audit columns.</summary>
internal sealed record SealedIntegrationRow(
    int Id,
    Guid RefId,
    int WorkspaceId,
    IntegrationType Type,
    IntegrationPreset? Preset,
    string ConfigJson,
    SealedSecret Secret);

internal interface IIntegrationStore
{
    Task<SealedIntegrationRow?> FindSealedAsync(Guid refId, CancellationToken ct);

    /// <summary>Stamps the integration as just used; the procedure throttles it to once a minute.</summary>
    Task TouchLastUsedAsync(int id, CancellationToken ct);
}

internal sealed class IntegrationStore(IConfiguration configuration) : BaseSqlProvider(configuration), IIntegrationStore
{
    public Task<SealedIntegrationRow?> FindSealedAsync(Guid refId, CancellationToken ct) =>
        ExecuteFindAsync(
            "dbo.Integration_GetSealed_By_RefId",
            p => p.AddWithValue("@RefId", refId),
            Map,
            ct);

    public Task TouchLastUsedAsync(int id, CancellationToken ct) =>
        ExecuteNonQueryAsync("dbo.Integration_TouchLastUsed", p => p.AddWithValue("@Id", id), ct);

    private static SealedIntegrationRow Map(SqlDataReader reader) => new(
        reader.GetInt32(reader.GetOrdinal("Id")),
        reader.GetGuid(reader.GetOrdinal("RefId")),
        reader.GetInt32(reader.GetOrdinal("WorkspaceId")),
        (IntegrationType)reader.GetByte(reader.GetOrdinal("Type")),
        reader.IsDBNull(reader.GetOrdinal("Preset")) ? null : (IntegrationPreset)reader.GetByte(reader.GetOrdinal("Preset")),
        reader.GetString(reader.GetOrdinal("ConfigJson")),
        new SealedSecret((byte[])reader["SecretCiphertext"], reader.GetByte(reader.GetOrdinal("KeyId"))));
}
