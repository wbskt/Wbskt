using Wbskt.Models.Billing;
using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
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
                p.AddWithValue("@ParentRunRefId", (object?)row.ParentRunRefId ?? DBNull.Value);
            },
            Map,
            new InvalidOperationException("Run_Create did not return a row."),
            ct
        );
    }

    public Task<RunStartRow> StartAsync(RunRow run, BranchRow rootBranch, string? triggerStartKey, CancellationToken ct)
        => StartAsync(run, rootBranch, triggerStartKey, 0, null, ct);

    public Task<RunStartRow> StartAsync(RunRow run, BranchRow rootBranch, string? triggerStartKey, int workspaceId, PlanCeiling? ceiling, CancellationToken ct)
        => StartAsync(run, rootBranch, triggerStartKey, workspaceId, ceiling, ActiveRunGuard.None, ct);

    public Task<RunStartRow> StartAsync(RunRow run, BranchRow rootBranch, string? triggerStartKey, int workspaceId, PlanCeiling? ceiling, ActiveRunGuard activeRunGuard, CancellationToken ct)
        => StartAsync(run, rootBranch, triggerStartKey, workspaceId, ceiling, activeRunGuard, null, ct);

    public async Task<RunStartRow> StartAsync(RunRow run, BranchRow rootBranch, string? triggerStartKey, int workspaceId, PlanCeiling? ceiling, ActiveRunGuard activeRunGuard, PlanCredits? creditAllowance, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.Run_Start",
            p =>
            {
                p.AddWithValue("@RefId", run.RefId);
                p.AddWithValue("@WorkflowDefinitionId", run.WorkflowDefinitionId);
                p.AddWithValue("@WorkflowRefId", run.WorkflowRefId);
                p.AddWithValue("@WorkflowVersion", run.WorkflowVersion);
                p.AddWithValue("@TriggerNodeId", run.TriggerNodeId);
                p.AddWithValue("@CorrelationKey", (object?)run.CorrelationKey ?? DBNull.Value);
                p.AddWithValue("@StartedAt", run.StartedAt);
                p.AddWithValue("@CreditBudget", run.CreditBudget);
                p.AddWithValue("@TriggerStartKey", (object?)triggerStartKey ?? DBNull.Value);
                p.AddWithValue("@BranchRefId", rootBranch.RefId);
                p.AddWithValue("@BranchLocalJson", rootBranch.LocalJson);
                p.AddWithValue("@ParentRunRefId", (object?)run.ParentRunRefId ?? DBNull.Value);
                p.AddWithValue("@ActiveRunGuard", (byte)activeRunGuard);
                p.AddWithValue("@IsTest", run.IsTest);
                p.AddWithValue("@StartCost", run.StartCost);
                if (ceiling is not null || creditAllowance is not null)
                {
                    p.AddWithValue("@WorkspaceId", workspaceId);
                    p.AddWithValue("@TenantId", (object?)(ceiling?.TenantId ?? creditAllowance?.TenantId) ?? DBNull.Value);
                }

                if (ceiling is not null)
                {
                    p.AddWithValue("@MaxActiveRuns", ceiling.Limit);
                }

                if (creditAllowance is not null)
                {
                    p.AddWithValue("@CreditAllowance", creditAllowance.RunsRefusedAt);
                }
            },
            MapStart,
            new InvalidOperationException("Run_Start did not return a row."),
            ct
        );
    }

    public async Task<Guid?> FindActiveTestRunAsync(Guid workflowRefId, CancellationToken ct)
    {
        var refId = await ExecuteScalarAsync<object>(
            "dbo.Run_GetActiveTestBy_WorkflowRefId",
            p => p.AddWithValue("@WorkflowRefId", workflowRefId),
            ct
        );
        return refId is Guid guid ? guid : null;
    }

    public async Task<RunStartRow?> FindByTriggerStartKeyAsync(string triggerStartKey, CancellationToken ct)
    {
        return await ExecuteFindAsync(
            "dbo.Run_GetBy_TriggerStartKey",
            p => p.AddWithValue("@TriggerStartKey", triggerStartKey),
            MapStart,
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

    public async Task<RunRow?> FindRowByRefIdAsync(Guid refId, CancellationToken ct)
    {
        return await ExecuteFindAsync(
            "dbo.Run_GetBy_RefId",
            p => p.AddWithValue("@RefId", refId),
            Map,
            ct
        );
    }

    public Task<IReadOnlyCollection<RunRow>> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct) =>
        ListByWorkflowAsync(workflowRefId, statusFilter, isTest: null, top, cursorId, ct);

    public async Task<IReadOnlyCollection<RunRow>> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, bool? isTest, int top, long? cursorId, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.Run_ListBy_WorkflowRefId",
            p =>
            {
                p.AddWithValue("@WorkflowRefId", workflowRefId);
                p.AddWithValue("@StatusFilter", (object?)statusFilter ?? DBNull.Value);
                p.AddWithValue("@Top", top);
                p.AddWithValue("@CursorId", (object?)cursorId ?? DBNull.Value);
                p.AddWithValue("@IsTest", (object?)isTest ?? DBNull.Value);
            },
            Map,
            ct
        );
    }

    public Task<IReadOnlyCollection<RunRow>> ListByWorkspaceAsync(int workspaceId, string? statusFilter, int top, long? cursorId, CancellationToken ct) =>
        ListByWorkspaceAsync(workspaceId, statusFilter, isTest: null, top, cursorId, ct);

    public async Task<IReadOnlyCollection<RunRow>> ListByWorkspaceAsync(int workspaceId, string? statusFilter, bool? isTest, int top, long? cursorId, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.Run_ListBy_WorkspaceId",
            p =>
            {
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.AddWithValue("@StatusFilter", (object?)statusFilter ?? DBNull.Value);
                p.AddWithValue("@Top", top);
                p.AddWithValue("@CursorId", (object?)cursorId ?? DBNull.Value);
                p.AddWithValue("@IsTest", (object?)isTest ?? DBNull.Value);
            },
            Map,
            ct
        );
    }

    public async Task<IReadOnlyCollection<RunListDetailRow>> GetListDetailsAsync(IReadOnlyCollection<int> runIds, CancellationToken ct)
    {
        if (runIds.Count == 0)
        {
            return [];
        }

        var ids = new DataTable();
        ids.Columns.Add("Id", typeof(int));
        foreach (var id in runIds.Distinct())
        {
            ids.Rows.Add(id);
        }

        return await ExecuteCollectionAsync(
            "dbo.Run_GetListDetails",
            p =>
            {
                var parameter = p.AddWithValue("@RunIds", ids);
                parameter.SqlDbType = SqlDbType.Structured;
                parameter.TypeName = "dbo.IdTableType";
            },
            r => new RunListDetailRow
            {
                RunId = r.GetInt32(r.GetOrdinal("RunId")),
                CreditsConsumed = r.IsDBNull(r.GetOrdinal("CreditsConsumed")) ? null : r.GetDecimal(r.GetOrdinal("CreditsConsumed")),
                FailedNodeId = r.IsDBNull(r.GetOrdinal("FailedNodeId")) ? null : r.GetGuid(r.GetOrdinal("FailedNodeId")),
                FailedErrorCode = r.IsDBNull(r.GetOrdinal("FailedErrorCode")) ? null : r.GetString(r.GetOrdinal("FailedErrorCode")),
                EndedNodeId = r.IsDBNull(r.GetOrdinal("EndedNodeId")) ? null : r.GetGuid(r.GetOrdinal("EndedNodeId")),
                CurrentNodeId = r.IsDBNull(r.GetOrdinal("CurrentNodeId")) ? null : r.GetGuid(r.GetOrdinal("CurrentNodeId")),
                CurrentAttempt = r.IsDBNull(r.GetOrdinal("CurrentAttempt")) ? null : r.GetInt32(r.GetOrdinal("CurrentAttempt")),
                CurrentMaxAttempts = r.IsDBNull(r.GetOrdinal("CurrentMaxAttempts")) ? null : r.GetInt32(r.GetOrdinal("CurrentMaxAttempts")),
                IsWaiting = r.GetBoolean(r.GetOrdinal("IsWaiting")),
                WaitingKind = r.IsDBNull(r.GetOrdinal("WaitingKind")) ? null : r.GetString(r.GetOrdinal("WaitingKind")),
                WaitingNodeId = r.IsDBNull(r.GetOrdinal("WaitingNodeId")) ? null : r.GetGuid(r.GetOrdinal("WaitingNodeId")),
                WaitingExpiresAt = r.IsDBNull(r.GetOrdinal("WaitingExpiresAt")) ? null : r.GetDateTime(r.GetOrdinal("WaitingExpiresAt")),
                WaitingSignalName = r.IsDBNull(r.GetOrdinal("WaitingSignalName")) ? null : r.GetString(r.GetOrdinal("WaitingSignalName"))
            },
            ct
        );
    }

    public async Task<RunStatsRow> GetStatsAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.Run_GetStatsBy_WorkflowRefId",
            p =>
            {
                p.AddWithValue("@WorkflowRefId", workflowRefId);
                p.AddWithValue("@FromUtc", fromUtc);
                p.AddWithValue("@ToUtc", toUtc);
            },
            MapStats,
            new InvalidOperationException("Run_GetStatsBy_WorkflowRefId did not return a row."),
            ct
        );
    }

    public async Task<RunStatsRow> GetWorkspaceStatsAsync(int workspaceId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.Run_GetStatsBy_WorkspaceId",
            p =>
            {
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.AddWithValue("@FromUtc", fromUtc);
                p.AddWithValue("@ToUtc", toUtc);
            },
            MapStats,
            new InvalidOperationException("Run_GetStatsBy_WorkspaceId did not return a row."),
            ct
        );
    }

    public async Task<IReadOnlyCollection<WorkflowRunSummaryRow>> GetPerWorkflowStatsAsync(int workspaceId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.Run_GetStatsPerWorkflowBy_WorkspaceId",
            p =>
            {
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.AddWithValue("@FromUtc", fromUtc);
                p.AddWithValue("@ToUtc", toUtc);
                p.AddWithValue("@Top", top);
            },
            MapWorkflowSummary,
            ct
        );
    }

    public async Task<IReadOnlyCollection<RunDailyStatsRow>> GetDailyStatsAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.Run_GetDailyStatsBy_WorkflowRefId",
            p =>
            {
                p.AddWithValue("@WorkflowRefId", workflowRefId);
                p.AddWithValue("@FromUtc", fromUtc);
                p.AddWithValue("@ToUtc", toUtc);
            },
            MapDailyStats,
            ct
        );
    }

    public async Task<IReadOnlyCollection<RunDailyStatsRow>> GetWorkspaceDailyStatsAsync(int workspaceId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.Run_GetDailyStatsBy_WorkspaceId",
            p =>
            {
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.AddWithValue("@FromUtc", fromUtc);
                p.AddWithValue("@ToUtc", toUtc);
            },
            MapDailyStats,
            ct
        );
    }

    internal static RunDailyStatsRow MapDailyStats(DbDataReader reader)
    {
        return new RunDailyStatsRow
        {
            WorkflowRefId = reader.GetGuid(reader.GetOrdinal("WorkflowRefId")),
            Day = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("Day"))),
            TotalRuns = reader.GetInt32(reader.GetOrdinal("TotalRuns")),
            SucceededCount = reader.GetInt32(reader.GetOrdinal("SucceededCount")),
            FailedCount = reader.GetInt32(reader.GetOrdinal("FailedCount")),
            CancelledCount = reader.GetInt32(reader.GetOrdinal("CancelledCount")),
            P50DurationMs = TryGetOrdinal(reader, "P50DurationMs") is { } p50Ordinal && !reader.IsDBNull(p50Ordinal) ? Convert.ToDouble(reader.GetValue(p50Ordinal)) : null
        };
    }

    internal static WorkflowRunSummaryRow MapWorkflowSummary(DbDataReader reader)
    {
        return new WorkflowRunSummaryRow
        {
            WorkflowRefId = reader.GetGuid(reader.GetOrdinal("WorkflowRefId")),
            TotalRuns = reader.GetInt32(reader.GetOrdinal("TotalRuns")),
            SucceededCount = reader.GetInt32(reader.GetOrdinal("SucceededCount")),
            FailedCount = reader.GetInt32(reader.GetOrdinal("FailedCount")),
            ActiveCount = reader.GetInt32(reader.GetOrdinal("ActiveCount")),
            WaitingCount = reader.GetInt32(reader.GetOrdinal("WaitingCount")),
            AvgDurationMs = Convert.ToDouble(reader.GetValue(reader.GetOrdinal("AvgDurationMs")))
        };
    }

    public async Task<IReadOnlyCollection<RunFailureBucketRow>> GetTopFailuresAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.Run_GetTopFailuresBy_WorkflowRefId",
            p =>
            {
                p.AddWithValue("@WorkflowRefId", workflowRefId);
                p.AddWithValue("@FromUtc", fromUtc);
                p.AddWithValue("@ToUtc", toUtc);
                p.AddWithValue("@Top", top);
            },
            MapFailureBucket,
            ct
        );
    }

    public async Task<IReadOnlyCollection<NodeTimingRow>> GetNodeTimingsAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.Run_GetNodeTimingsBy_WorkflowRefId",
            p =>
            {
                p.AddWithValue("@WorkflowRefId", workflowRefId);
                p.AddWithValue("@FromUtc", fromUtc);
                p.AddWithValue("@ToUtc", toUtc);
                p.AddWithValue("@Top", top);
            },
            MapNodeTiming,
            ct
        );
    }

    internal static RunStatsRow MapStats(SqlDataReader reader)
    {
        return new RunStatsRow
        {
            TotalRuns = reader.GetInt32(reader.GetOrdinal("TotalRuns")),
            SucceededCount = ReadInt(reader, "SucceededCount"),
            FailedCount = ReadInt(reader, "FailedCount"),
            PartiallyFailedCount = ReadInt(reader, "PartiallyFailedCount"),
            CancelledCount = ReadInt(reader, "CancelledCount"),
            OutOfCreditsCount = ReadInt(reader, "OutOfCreditsCount"),
            ActiveCount = ReadInt(reader, "ActiveCount"),
            WaitingCount = ReadInt(reader, "WaitingCount"),
            P50DurationMs = ReadDouble(reader, "P50DurationMs"),
            P95DurationMs = ReadDouble(reader, "P95DurationMs"),
            MaxDurationMs = ReadDouble(reader, "MaxDurationMs"),
            AvgDurationMs = ReadDouble(reader, "AvgDurationMs")
        };
    }

    internal static RunFailureBucketRow MapFailureBucket(SqlDataReader reader)
    {
        int nodeOrdinal = reader.GetOrdinal("NodeId");

        return new RunFailureBucketRow
        {
            ErrorCode = reader.GetString(reader.GetOrdinal("ErrorCode")),
            NodeId = reader.IsDBNull(nodeOrdinal) ? null : reader.GetGuid(nodeOrdinal),
            Occurrences = reader.GetInt32(reader.GetOrdinal("Occurrences")),
            LastSeenAt = reader.GetDateTime(reader.GetOrdinal("LastSeenAt"))
        };
    }

    internal static NodeTimingRow MapNodeTiming(SqlDataReader reader)
    {
        return new NodeTimingRow
        {
            NodeId = reader.GetGuid(reader.GetOrdinal("NodeId")),
            Executions = reader.GetInt32(reader.GetOrdinal("Executions")),
            FailureCount = ReadInt(reader, "FailureCount"),
            AvgDurationMs = ReadDouble(reader, "AvgDurationMs"),
            MaxDurationMs = ReadDouble(reader, "MaxDurationMs")
        };
    }

    // An empty window aggregates to NULL rather than zero, so every aggregate column is read
    // defensively - a workflow with no runs yet must report zeroes, not blow up.
    private static int ReadInt(SqlDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static double ReadDouble(SqlDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? 0d : Convert.ToDouble(reader.GetValue(ordinal));
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

    public async Task<IReadOnlyCollection<RunRow>> GetEndedChildrenAwaitedAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.Run_GetEndedChildrenAwaited",
            p =>
            {
                p.AddWithValue("@CutoffUtc", cutoffUtc);
                p.AddWithValue("@BatchSize", batchSize);
            },
            Map,
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

    public async Task<IReadOnlyCollection<RunRow>> GetAllByStatusAsync(string status, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.Run_GetAllBy_Status",
            p => p.AddWithValue("@Status", status),
            Map,
            ct
        );
    }

    public async Task<IReadOnlyCollection<RunRow>> GetActiveChildrenAsync(Guid parentRunRefId, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.Run_GetActiveChildrenBy_ParentRefId",
            p => p.AddWithValue("@ParentRunRefId", parentRunRefId),
            Map,
            ct
        );
    }

    public async Task SetOutputAsync(long runId, string outputJson, CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            "dbo.Run_SetOutput",
            p =>
            {
                p.AddWithValue("@RunId", checked((int)runId));
                p.AddWithValue("@OutputJson", outputJson);
            },
            ct
        );
    }

    public async Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct)
    {
        var result = await ExecuteScalarAsync<object>(
            "dbo.Run_TransitionStatus",
            p =>
            {
                p.AddWithValue("@RunId", checked((int)runId));
                p.AddWithValue("@FromStatus", fromStatus);
                p.AddWithValue("@ToStatus", toStatus);
                p.AddWithValue("@CancellationRequestedAt", (object?)cancellationRequestedAt ?? DBNull.Value);
                p.AddWithValue("@CancellationReason", (object?)cancellationReason ?? DBNull.Value);
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

    private static RunStartRow MapStart(DbDataReader reader) => new(
        Map(reader),
        reader.GetInt64(reader.GetOrdinal("RootBranchId")),
        reader.GetBoolean(reader.GetOrdinal("Created")));

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
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            ParentRunRefId = TryGetOrdinal(reader, "ParentRunRefId") is { } parentOrdinal && !reader.IsDBNull(parentOrdinal) ? reader.GetGuid(parentOrdinal) : null,
            OutputJson = TryGetOrdinal(reader, "OutputJson") is { } outputOrdinal && !reader.IsDBNull(outputOrdinal) ? reader.GetString(outputOrdinal) : null,
            Number = TryGetOrdinal(reader, "Number") is { } numberOrdinal ? reader.GetInt32(numberOrdinal) : 0,
            IsTest = TryGetOrdinal(reader, "IsTest") is { } isTestOrdinal && reader.GetBoolean(isTestOrdinal)
        };
    }

    // The run-list procs leave OutputJson out (it is unbounded and no page shows it), and only the procs
    // behind the run pages return Number and IsTest, so these are read only where the result set carries them
    // rather than making every proc return them.
    private static int? TryGetOrdinal(DbDataReader reader, string column)
    {
        for (int i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), column, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return null;
    }
}
