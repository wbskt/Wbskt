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
