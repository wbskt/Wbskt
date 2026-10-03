using System.Data.Common;
using Wbskt.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.Providers;

internal sealed class ClientPresenceCheckProvider : BaseSqlProvider, IClientPresenceCheckProvider
{
    public ClientPresenceCheckProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<IReadOnlyCollection<string>> GetTriggerKeysAsync(string keyPrefix, int workspaceId, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.TriggerRegistration_GetPresenceKeysBy_Prefix",
            p =>
            {
                p.AddWithValue("@KeyPrefix", keyPrefix);
                p.AddWithValue("@WorkspaceId", workspaceId);
            },
            reader => reader.GetString(reader.GetOrdinal("TriggerKey")),
            ct
        );
    }

    public async Task InsertAsync(string triggerKey, Guid clientRefId, string state, DateTime changedAt, DateTime dueAt, CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            "dbo.ClientPresenceCheck_Insert",
            p =>
            {
                p.AddWithValue("@TriggerKey", triggerKey);
                p.AddWithValue("@ClientRefId", clientRefId);
                p.AddWithValue("@State", state);
                p.AddWithValue("@ChangedAt", changedAt);
                p.AddWithValue("@DueAt", dueAt);
            },
            ct
        );
    }

    public async Task<IReadOnlyCollection<ClientPresenceCheckRow>> LeaseDueAsync(int leaseSec, int batch, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.ClientPresenceCheck_LeaseDue",
            p =>
            {
                p.AddWithValue("@LeaseSec", leaseSec);
                p.AddWithValue("@Batch", batch);
            },
            Map,
            ct
        );
    }

    public async Task DeleteByIdAsync(int id, CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            "dbo.ClientPresenceCheck_DeleteById",
            p => p.AddWithValue("@Id", id),
            ct
        );
    }

    private static ClientPresenceCheckRow Map(DbDataReader reader)
    {
        return new ClientPresenceCheckRow
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            TriggerKey = reader.GetString(reader.GetOrdinal("TriggerKey")),
            ClientRefId = reader.GetGuid(reader.GetOrdinal("ClientRefId")),
            State = reader.GetString(reader.GetOrdinal("State")),
            ChangedAt = reader.GetDateTime(reader.GetOrdinal("ChangedAt")),
            DueAt = reader.GetDateTime(reader.GetOrdinal("DueAt")),
            ClientId = NullableInt(reader, "ClientId"),
            ClientWorkspaceId = NullableInt(reader, "ClientWorkspaceId"),
            ClientIsConnected = reader.IsDBNull(reader.GetOrdinal("ClientIsConnected")) ? null : reader.GetBoolean(reader.GetOrdinal("ClientIsConnected")),
            ClientConnectedAt = NullableDateTime(reader, "ClientConnectedAt"),
            ClientLastActivityAt = NullableDateTime(reader, "ClientLastActivityAt")
        };
    }

    private static int? NullableInt(DbDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static DateTime? NullableDateTime(DbDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }
}
