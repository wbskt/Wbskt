using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

internal sealed class TriggerRegistrationProvider : BaseSqlProvider, ITriggerRegistrationProvider
{
    public TriggerRegistrationProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<TriggerRegistrationRow> InsertAsync(TriggerRegistrationRow row, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.TriggerRegistration_Insert",
            p =>
            {
                p.AddWithValue("@WorkflowDefinitionId", row.WorkflowDefinitionId);
                p.AddWithValue("@WorkflowRefId", row.WorkflowRefId);
                p.AddWithValue("@WorkflowVersion", row.WorkflowVersion);
                p.AddWithValue("@TriggerNodeId", row.TriggerNodeId);
                p.AddWithValue("@TriggerKind", row.TriggerKind);
                p.AddWithValue("@TriggerKey", row.TriggerKey);
                p.AddWithValue("@CorrelationExpression", (object?)row.CorrelationExpression ?? DBNull.Value);
                p.AddWithValue("@ConcurrencyPolicy", (object?)row.ConcurrencyPolicy ?? DBNull.Value);
                p.AddWithValue("@FilterExpression", (object?)row.FilterExpression ?? DBNull.Value);
                p.AddWithValue("@WebhookSecret", (object?)row.WebhookSecret ?? DBNull.Value);
            },
            Map,
            new InvalidOperationException("TriggerRegistration_Insert did not return a row."),
            ct
        );
    }

    public async Task<IReadOnlyCollection<TriggerRegistrationRow>> GetByTriggerKeyAsync(string triggerKey, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.TriggerRegistration_GetBy_TriggerKey",
            p => p.AddWithValue("@TriggerKey", triggerKey),
            Map,
            ct
        );
    }

    public async Task<IReadOnlyCollection<TriggerRegistrationRow>> GetActiveByChannelKeysAsync(string channelKind, IReadOnlyCollection<string> channelKeys, CancellationToken ct)
    {
        if (channelKeys == null || channelKeys.Count == 0)
        {
            return Array.Empty<TriggerRegistrationRow>();
        }

        string jsonKeys = JsonSerializer.Serialize(channelKeys);

        return await ExecuteCollectionAsync(
            "dbo.TriggerRegistration_GetAllByKeys",
            p =>
            {
                p.AddWithValue("@TriggerKind", channelKind);
                p.AddWithValue("@KeysJson", jsonKeys);
            },
            Map,
            ct
        );
    }

    public async Task<IReadOnlyCollection<TriggerRegistrationRow>> GetAllByWorkflowDefinitionIdAsync(int workflowDefinitionId, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.TriggerRegistration_GetAllBy_WorkflowDefinitionId",
            p => p.AddWithValue("@WorkflowDefinitionId", workflowDefinitionId),
            Map,
            ct
        );
    }

    public async Task DeleteAllByWorkflowDefinitionIdAsync(int workflowDefinitionId, CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            "dbo.TriggerRegistration_DeleteAllBy_WorkflowDefinitionId",
            p => p.AddWithValue("@WorkflowDefinitionId", workflowDefinitionId),
            ct
        );
    }

    internal static TriggerRegistrationRow Map(DbDataReader reader)
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
            CorrelationExpression = reader.IsDBNull(reader.GetOrdinal("CorrelationExpression")) ? null : reader.GetString(reader.GetOrdinal("CorrelationExpression")),
            ConcurrencyPolicy = reader.IsDBNull(reader.GetOrdinal("ConcurrencyPolicy")) ? null : reader.GetString(reader.GetOrdinal("ConcurrencyPolicy")),
            FilterExpression = reader.IsDBNull(reader.GetOrdinal("FilterExpression")) ? null : reader.GetString(reader.GetOrdinal("FilterExpression")),
            WebhookSecret = reader.IsDBNull(reader.GetOrdinal("WebhookSecret")) ? null : reader.GetString(reader.GetOrdinal("WebhookSecret")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
        };
    }
}
