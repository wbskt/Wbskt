using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Workflow.Abstraction.Integrations;

namespace Wbskt.Management.Host.Providers;

internal sealed class IntegrationProvider(IConfiguration configuration) : BaseSqlProvider(configuration), IIntegrationProvider
{
    public Task<IReadOnlyCollection<Integration>> GetAllAsync(int workspaceId, IntegrationType? type, CancellationToken ct) =>
        ExecuteCollectionAsync(
            "dbo.Integration_GetAll_By_WorkspaceId",
            p =>
            {
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.AddWithValue("@Type", type is null ? DBNull.Value : (byte)type.Value);
            },
            Map,
            ct);

    public Task<Integration?> FindByRefIdAsync(Guid refId, CancellationToken ct) =>
        ExecuteFindAsync("dbo.Integration_GetBy_RefId", p => p.AddWithValue("@RefId", refId), Map, ct);

    public Task InsertAsync(Integration integration, int maxPerWorkspace, CancellationToken ct) =>
        ExecuteNonQueryAsync("dbo.Integration_Create", p =>
        {
            p.AddWithValue("@RefId", integration.RefId);
            p.AddWithValue("@WorkspaceId", integration.WorkspaceId);
            p.AddWithValue("@Type", (byte)integration.Type);
            p.AddWithValue("@Preset", integration.Preset is null ? DBNull.Value : (byte)integration.Preset.Value);
            p.AddWithValue("@Name", integration.Name);
            p.AddWithValue("@ConfigJson", integration.ConfigJson);
            p.Add("@SecretCiphertext", SqlDbType.VarBinary, -1).Value = integration.SecretCiphertext;
            p.AddWithValue("@KeyId", integration.KeyId);
            p.AddWithValue("@SecretHint", (object?)integration.SecretHint ?? DBNull.Value);
            p.AddWithValue("@CreatedBy", integration.CreatedBy);
            p.AddWithValue("@CreatedByRefId", (object?)integration.CreatedByRefId ?? DBNull.Value);
            p.AddWithValue("@MaxPerWorkspace", maxPerWorkspace);
        }, ct);

    public Task UpdateAsync(int workspaceId, int id, string name, string configJson, bool configChanged, byte[]? ciphertext, byte? keyId, string? secretHint, bool secretRotated, CancellationToken ct) =>
        ExecuteNonQueryAsync("dbo.Integration_Update", p =>
        {
            p.AddWithValue("@Id", id);
            p.AddWithValue("@WorkspaceId", workspaceId);
            p.AddWithValue("@Name", name);
            p.AddWithValue("@ConfigJson", configJson);
            p.AddWithValue("@ConfigChanged", configChanged);
            p.Add("@SecretCiphertext", SqlDbType.VarBinary, -1).Value = (object?)ciphertext ?? DBNull.Value;
            p.AddWithValue("@KeyId", keyId is null ? DBNull.Value : keyId.Value);
            p.AddWithValue("@SecretHint", (object?)secretHint ?? DBNull.Value);
            p.AddWithValue("@SecretRotated", secretRotated);
        }, ct);

    public async Task<bool> RecordTestAsync(int id, DateTime expectedUpdatedAt, bool ok, string? code, string? error, string? metadata, CancellationToken ct)
    {
        var recorded = await ExecuteScalarAsync<object>("dbo.Integration_RecordTest", p =>
        {
            p.AddWithValue("@Id", id);
            p.Add("@ExpectedUpdatedAt", SqlDbType.DateTime2).Value = expectedUpdatedAt;
            p.AddWithValue("@Ok", ok);
            p.AddWithValue("@Code", (object?)code ?? DBNull.Value);
            p.AddWithValue("@Error", (object?)error ?? DBNull.Value);
            p.AddWithValue("@Metadata", (object?)metadata ?? DBNull.Value);
        }, ct);

        return recorded is not null && Convert.ToInt32(recorded) > 0;
    }

    public Task<IReadOnlyCollection<IntegrationUse>> GetUsageAsync(int id, CancellationToken ct) =>
        ExecuteCollectionAsync("dbo.Integration_GetUsage", p => p.AddWithValue("@IntegrationId", id), MapUse, ct);

    public Task<IReadOnlyCollection<IntegrationUse>> DeleteAsync(int workspaceId, int id, CancellationToken ct) =>
        ExecuteCollectionAsync("dbo.Integration_Delete", p =>
        {
            p.AddWithValue("@Id", id);
            p.AddWithValue("@WorkspaceId", workspaceId);
        }, MapUse, ct);

    private static IntegrationUse MapUse(SqlDataReader reader) => new(
        reader.GetGuid(reader.GetOrdinal("WorkflowRefId")),
        reader.GetString(reader.GetOrdinal("WorkflowName")),
        reader.GetGuid(reader.GetOrdinal("NodeId")),
        reader.GetString(reader.GetOrdinal("NodeName")),
        reader.GetBoolean(reader.GetOrdinal("IsEnabled")));

    private static Integration Map(SqlDataReader reader)
    {
        T? Nullable<T>(string column) where T : struct =>
            reader.IsDBNull(reader.GetOrdinal(column)) ? null : reader.GetFieldValue<T>(reader.GetOrdinal(column));

        string? Text(string column) =>
            reader.IsDBNull(reader.GetOrdinal(column)) ? null : reader.GetString(reader.GetOrdinal(column));

        return new Integration
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            RefId = reader.GetGuid(reader.GetOrdinal("RefId")),
            WorkspaceId = reader.GetInt32(reader.GetOrdinal("WorkspaceId")),
            Type = (IntegrationType)reader.GetByte(reader.GetOrdinal("Type")),
            Preset = Nullable<byte>("Preset") is { } preset ? (IntegrationPreset)preset : null,
            Name = reader.GetString(reader.GetOrdinal("Name")),
            ConfigJson = reader.GetString(reader.GetOrdinal("ConfigJson")),
            SecretCiphertext = (byte[])reader["SecretCiphertext"],
            KeyId = reader.GetByte(reader.GetOrdinal("KeyId")),
            SecretHint = Text("SecretHint"),
            Metadata = Text("Metadata"),
            LastTestAt = Nullable<DateTime>("LastTestAt"),
            LastTestOk = Nullable<bool>("LastTestOk"),
            LastTestCode = Text("LastTestCode"),
            LastTestError = Text("LastTestError"),
            LastUsedAt = Nullable<DateTime>("LastUsedAt"),
            CreatedBy = reader.GetInt32(reader.GetOrdinal("CreatedBy")),
            CreatedByRefId = Nullable<Guid>("CreatedByRefId"),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
            SecretRotatedAt = Nullable<DateTime>("SecretRotatedAt"),
            UsageWorkflows = reader.GetInt32(reader.GetOrdinal("UsageWorkflows")),
            UsageSteps = reader.GetInt32(reader.GetOrdinal("UsageSteps"))
        };
    }
}
