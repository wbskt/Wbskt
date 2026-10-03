using System.Data;
using System.Data.Common;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models.Triggers;

namespace Wbskt.Workflow.Engine.Host.Providers;

internal sealed class ClientHoldStateProvider : BaseSqlProvider, IClientHoldStateProvider
{
    public ClientHoldStateProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<IReadOnlyCollection<TriggerRegistrationRow>> GetRegistrationsAsync(Guid clientRefId, string messageType, int workspaceId, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.TriggerRegistration_GetClientHoldBy_Prefix",
            p =>
            {
                p.AddWithValue("@TypePrefix", ClientHoldTriggerKey.Prefix(clientRefId, messageType));
                p.AddWithValue("@WildcardPrefix", ClientHoldTriggerKey.Prefix(clientRefId, "*"));
                p.AddWithValue("@WorkspaceId", workspaceId);
            },
            MapRegistration,
            ct
        );
    }

    public async Task RecordAsync(string triggerKey, bool matches, DateTime eventAt, int holdSeconds, string payload, CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            "dbo.ClientHoldState_Record",
            p =>
            {
                p.AddWithValue("@TriggerKey", triggerKey);
                p.AddWithValue("@Matches", matches);
                // DATETIME2 explicitly: AddWithValue would send a DateTime as SQL datetime, rounded to
                // 1/300 s, and two messages a few milliseconds apart could swap order.
                p.Add("@EventAt", SqlDbType.DateTime2).Value = eventAt;
                p.AddWithValue("@HoldSeconds", holdSeconds);
                p.AddWithValue("@Payload", payload);
            },
            ct
        );
    }

    public async Task<IReadOnlyCollection<ClientHoldStateRow>> LeaseDueAsync(int leaseSec, int batch, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.ClientHoldState_LeaseDue",
            p =>
            {
                p.AddWithValue("@LeaseSec", leaseSec);
                p.AddWithValue("@Batch", batch);
            },
            reader => new ClientHoldStateRow
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                TriggerKey = reader.GetString(reader.GetOrdinal("TriggerKey")),
                SinceAt = reader.GetDateTime(reader.GetOrdinal("SinceAt")),
                DueAt = reader.GetDateTime(reader.GetOrdinal("DueAt")),
                Payload = reader.GetString(reader.GetOrdinal("Payload"))
            },
            ct
        );
    }

    public async Task MarkFiredAsync(int id, DateTime sinceAt, CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            "dbo.ClientHoldState_MarkFired",
            p =>
            {
                p.AddWithValue("@Id", id);
                p.Add("@SinceAt", SqlDbType.DateTime2).Value = sinceAt;
            },
            ct
        );
    }

    public async Task DeleteByIdAsync(int id, CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            "dbo.ClientHoldState_DeleteById",
            p => p.AddWithValue("@Id", id),
            ct
        );
    }

    private static TriggerRegistrationRow MapRegistration(DbDataReader reader)
    {
        return new TriggerRegistrationRow
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            WorkflowDefinitionId = reader.GetInt32(reader.GetOrdinal("WorkflowDefinitionId")),
            WorkflowRefId = reader.GetGuid(reader.GetOrdinal("WorkflowRefId")),
            WorkflowVersion = reader.GetInt32(reader.GetOrdinal("WorkflowVersion")),
            TriggerNodeId = reader.GetGuid(reader.GetOrdinal("TriggerNodeId")),
            TriggerKind = reader.GetString(reader.GetOrdinal("TriggerKind")),
            TriggerKey = reader.GetString(reader.GetOrdinal("TriggerKey")),
            CorrelationExpression = NullableString(reader, "CorrelationExpression"),
            ConcurrencyPolicy = reader.GetString(reader.GetOrdinal("ConcurrencyPolicy")),
            FilterExpression = NullableString(reader, "FilterExpression"),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
        };
    }

    private static string? NullableString(DbDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
