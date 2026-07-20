using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

internal sealed class RunProvider : BaseSqlProvider, IRunProvider
{
    public RunProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<RunRow> CreateAsync(RunRow row, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.Run_Create",
            p =>
            {
                p.AddWithValue("@RefId", row.RefId);
                p.AddWithValue("@WorkflowDefinitionId", row.WorkflowDefinitionId);
                p.AddWithValue("@WorkflowRefId", row.WorkflowRefId);
                p.AddWithValue("@WorkflowVersion", row.WorkflowVersion);
                p.AddWithValue("@TriggerNodeId", row.TriggerNodeId);
                p.AddWithValue("@CorrelationKey", (object?)row.CorrelationKey ?? DBNull.Value);
                p.AddWithValue("@StartedAt", row.StartedAt);
                p.AddWithValue("@CreditBudget", row.CreditBudget);
            },
            Map,
            new InvalidOperationException("Run_Create did not return a row."),
            ct
        );
    }

    public async Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct)
    {
        var result = await ExecuteScalarAsync<object>(
            "dbo.Run_FindBy_RefId",
            p => p.AddWithValue("@RefId", refId),
            ct
        );
        return result is int id ? id : (result == null ? null : Convert.ToInt32(result));
    }

    public async Task<RunRow> GetByIdAsync(long runId, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.Run_GetById",
            p => p.AddWithValue("@Id", checked((int)runId)),
            Map,
            new KeyNotFoundException($"Run with Id={runId} not found."),
            ct
        );
    }

    public async Task<RunRow> GetByRefIdAsync(Guid refId, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.Run_GetBy_RefId",
            p => p.AddWithValue("@RefId", refId),
            Map,
            new KeyNotFoundException($"Run with RefId={refId} not found."),
            ct
        );
    }

    public async Task<IReadOnlyCollection<RunRow>> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.Run_ListBy_WorkflowRefId",
            p =>
            {
                p.AddWithValue("@WorkflowRefId", workflowRefId);
                p.AddWithValue("@StatusFilter", (object?)statusFilter ?? DBNull.Value);
                p.AddWithValue("@Top", top);
                p.AddWithValue("@CursorId", (object?)cursorId ?? DBNull.Value);
            },
            Map,
            ct
        );
    }

    public async Task<IReadOnlyCollection<RunRow>> GetActiveByWorkflowRefIdCorrelationKeyAsync(Guid workflowRefId, string correlationKey, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.Run_GetActiveBy_WorkflowRefId_CorrelationKey",
            p =>
            {
                p.AddWithValue("@WorkflowRefId", workflowRefId);
                p.AddWithValue("@CorrelationKey", correlationKey);
            },
            Map,
            ct
        );
    }

    public async Task<IReadOnlyCollection<RunRow>> GetActiveByCorrelationAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.Run_GetActiveBy_Correlation",
            p =>
            {
                p.AddWithValue("@WorkflowRefId", workflowRefId);
                p.AddWithValue("@TriggerNodeId", triggerNodeId);
                p.AddWithValue("@CorrelationKey", correlationKey);
            },
            Map,
            ct
        );
    }

    public async Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.Run_UpdateStatus",
            p =>
            {
                p.AddWithValue("@RefId", refId);
                p.AddWithValue("@Status", status);
                p.AddWithValue("@CompletedAt", (object?)completedAt ?? DBNull.Value);
                p.AddWithValue("@CancellationRequestedAt", (object?)cancellationRequestedAt ?? DBNull.Value);
                p.AddWithValue("@CancellationReason", (object?)cancellationReason ?? DBNull.Value);
            },
            Map,
            new KeyNotFoundException($"Run with RefId={refId} not found."),
            ct
        );
    }

    public async Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.Run_GetStuck",
            p =>
            {
                p.AddWithValue("@CutoffUtc", cutoffUtc);
                p.AddWithValue("@BatchSize", batchSize);
            },
            Map,
            ct
        );
    }

    public async Task<long> CountByStatusAsync(string status, CancellationToken ct)
    {
        var result = await ExecuteScalarAsync<object>(
            "dbo.Run_CountByStatus",
            p => p.AddWithValue("@Status", status),
            ct
        );
        return result is long count ? count : Convert.ToInt64(result ?? 0L);
    }

    public async Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, CancellationToken ct)
    {
        var result = await ExecuteScalarAsync<object>(
            "dbo.Run_TransitionStatus",
            p =>
            {
                p.AddWithValue("@RunId", checked((int)runId));
                p.AddWithValue("@FromStatus", fromStatus);
                p.AddWithValue("@ToStatus", toStatus);
            },
            ct
        );
        return result is int rowsAffected && rowsAffected > 0;
    }

    public async Task<(bool Transitioned, RunRow Run)> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.Run_SetTerminal",
            p =>
            {
                p.AddWithValue("@RunId", checked((int)runId));
                p.AddWithValue("@Status", status);
                p.AddWithValue("@CompletedAt", completedAt);
            },
            reader => (reader.GetBoolean(reader.GetOrdinal("Transitioned")), Map(reader)),
            new KeyNotFoundException($"Run with Id={runId} not found."),
            ct
        );
    }

    internal static RunRow Map(DbDataReader reader)
    {
        return new RunRow
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            RefId = reader.GetGuid(reader.GetOrdinal("RefId")),
            WorkflowDefinitionId = reader.GetInt32(reader.GetOrdinal("WorkflowDefinitionId")),
            WorkflowRefId = reader.GetGuid(reader.GetOrdinal("WorkflowRefId")),
            WorkflowVersion = reader.GetInt32(reader.GetOrdinal("WorkflowVersion")),
            TriggerNodeId = reader.GetGuid(reader.GetOrdinal("TriggerNodeId")),
            CorrelationKey = reader.IsDBNull(reader.GetOrdinal("CorrelationKey")) ? null : reader.GetString(reader.GetOrdinal("CorrelationKey")),
            Status = reader.GetString(reader.GetOrdinal("Status")),
            StartedAt = reader.GetDateTime(reader.GetOrdinal("StartedAt")),
            CompletedAt = reader.IsDBNull(reader.GetOrdinal("CompletedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("CompletedAt")),
            CancellationRequestedAt = reader.IsDBNull(reader.GetOrdinal("CancellationRequestedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("CancellationRequestedAt")),
            CancellationReason = reader.IsDBNull(reader.GetOrdinal("CancellationReason")) ? null : reader.GetString(reader.GetOrdinal("CancellationReason")),
            CreditBudget = reader.GetDecimal(reader.GetOrdinal("CreditBudget")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
        };
    }
}
