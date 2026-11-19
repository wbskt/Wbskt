using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Providers;

internal sealed class BranchProvider : BaseSqlProvider, IBranchProvider
{
    public BranchProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct)
    {
        return (await CreateIfAbsentAsync(row, ct)).Row;
    }

    public async Task<(BranchRow Row, bool Created)> CreateIfAbsentAsync(BranchRow row, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.Branch_InsertIfAbsent",
            p => AddRowParameters(p, row),
            reader => (Map(reader), reader.GetBoolean(reader.GetOrdinal("Inserted"))),
            new InvalidOperationException("Branch_InsertIfAbsent did not return a row."),
            ct
        );
    }

    public async Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.Branch_GetById",
            p => p.AddWithValue("@Id", branchId),
            Map,
            new KeyNotFoundException($"Branch with Id={branchId} not found."),
            ct
        );
    }

    public async Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.Branch_GetBy_RefId",
            p => p.AddWithValue("@RefId", refId),
            Map,
            new KeyNotFoundException($"Branch with RefId={refId} not found."),
            ct
        );
    }

    public async Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.Branch_GetAllBy_RunId",
            p => p.AddWithValue("@RunId", runId),
            Map,
            ct
        );
    }

    public Task<IReadOnlyCollection<string>> GetStatusesByRunIdAsync(int runId, CancellationToken ct) =>
        ExecuteCollectionAsync(
            "dbo.Branch_GetStatuses_By_RunId",
            p => p.AddWithValue("@RunId", runId),
            reader => reader.GetString(reader.GetOrdinal("Status")),
            ct);

    public Task<IReadOnlyCollection<RunningBranch>> GetRunningBranchesAsync(long afterId, int batchSize, CancellationToken ct) =>
        ExecuteCollectionAsync(
            "dbo.Branch_GetRunning",
            p =>
            {
                p.AddWithValue("@AfterId", afterId);
                p.AddWithValue("@BatchSize", batchSize);
            },
            reader => new RunningBranch(
                reader.GetInt64(reader.GetOrdinal("Id")),
                reader.GetInt32(reader.GetOrdinal("RunId")),
                reader.GetUtcDateTime("UpdatedAt"),
                reader.GetString(reader.GetOrdinal("RunStatus")),
                reader.GetNullableString("RunCancellationReason")),
            ct);

    public async Task<IReadOnlyCollection<BranchRow>> GetWaitingWithoutBookmarkAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.Branch_GetWaitingWithoutBookmark",
            p =>
            {
                p.AddWithValue("@CutoffUtc", cutoffUtc);
                p.AddWithValue("@BatchSize", batchSize);
            },
            Map,
            ct
        );
    }

    public async Task<IReadOnlyCollection<WokenBranch>> GetWokenAsync(long afterId, int batchSize, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.Branch_GetWoken",
            p =>
            {
                p.AddWithValue("@AfterId", afterId);
                p.AddWithValue("@BatchSize", batchSize);
            },
            MapWoken,
            ct
        );
    }

    public async Task<IReadOnlyCollection<WokenBranch>> GetWokenNotWrittenSinceAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.Branch_GetWoken",
            p =>
            {
                p.AddWithValue("@AfterId", 0L);
                p.AddWithValue("@BatchSize", batchSize);
                p.AddWithValue("@UpdatedBefore", cutoffUtc);
            },
            MapWoken,
            ct
        );
    }

    public Task<BranchRow> UpdatePointerAsync(long branchId, Guid currentNodeId, string status, string localJson, CancellationToken ct)
    {
        return UpdateBranchAsync(branchId, currentNodeId, status, localJson, ct);
    }

    public Task<BranchRow> SetCompletedAsync(long branchId, CancellationToken ct)
    {
        return UpdateBranchAsync(branchId, null, BranchStatus.Completed, null, ct);
    }

    public async Task<bool> EndIfLiveAsync(long branchId, string toStatus, string? errorJson, CancellationToken ct)
    {
        var result = await ExecuteScalarAsync<object>(
            "dbo.Branch_EndIfLive",
            p =>
            {
                p.AddWithValue("@Id", branchId);
                p.AddWithValue("@ToStatus", toStatus);
                p.AddWithValue("@ErrorJson", (object?)errorJson ?? DBNull.Value);
            },
            ct
        );
        return result is int rowsAffected && rowsAffected > 0;
    }

    private async Task<BranchRow> UpdateBranchAsync(long branchId, Guid? currentNodeId, string status, string? localJson, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.Branch_Update",
            p =>
            {
                p.AddWithValue("@Id", branchId);
                p.AddWithValue("@NodeId", (object?)currentNodeId ?? DBNull.Value);
                p.AddWithValue("@Status", status);
                p.AddWithValue("@LocalJson", (object?)localJson ?? DBNull.Value);
            },
            Map,
            new KeyNotFoundException($"Branch with Id={branchId} not found."),
            ct
        );
    }

    public async Task<int> CancelWaitingBranchesAsync(int runId, CancellationToken ct)
    {
        return await ExecuteNonQueryResultAsync(
            "dbo.Branch_CancelWaiting",
            p => p.AddWithValue("@RunId", runId),
            ct
        );
    }

    private static void AddRowParameters(SqlParameterCollection p, BranchRow row)
    {
        p.AddWithValue("@RefId", row.RefId);
        p.AddWithValue("@RunId", row.RunId);
        p.AddWithValue("@ParentBranchId", (object?)row.ParentBranchId ?? DBNull.Value);
        p.AddWithValue("@ForkCohortId", (object?)row.ForkCohortId ?? DBNull.Value);
        p.AddWithValue("@NodeId", row.NodeId);
        p.AddWithValue("@Status", row.Status);
        p.AddWithValue("@PendingTakePort", (object?)row.PendingTakePort ?? DBNull.Value);
        p.AddWithValue("@LocalJson", row.LocalJson);
    }

    internal static BranchRow Map(DbDataReader reader)
    {
        return new BranchRow
        {
            Id = reader.GetInt64(reader.GetOrdinal("Id")),
            RefId = reader.GetGuid(reader.GetOrdinal("RefId")),
            RunId = reader.GetInt32(reader.GetOrdinal("RunId")),
            ParentBranchId = reader.GetNullableGuid("ParentBranchId"),
            ForkCohortId = reader.GetNullableGuid("ForkCohortId"),
            NodeId = reader.GetGuid(reader.GetOrdinal("NodeId")),
            Status = reader.GetString(reader.GetOrdinal("Status")),
            PendingTakePort = reader.GetNullableString("PendingTakePort"),
            LocalJson = reader.GetString(reader.GetOrdinal("LocalJson")),
            CreatedAt = reader.GetUtcDateTime("CreatedAt"),
            UpdatedAt = reader.GetUtcDateTime("UpdatedAt"),
            RowVersion = (byte[])reader.GetValue(reader.GetOrdinal("RowVersion"))
        };
    }

    private static WokenBranch MapWoken(DbDataReader reader) => new(
        reader.GetInt64(reader.GetOrdinal("Id")),
        reader.GetInt32(reader.GetOrdinal("RunId")),
        (byte[])reader.GetValue(reader.GetOrdinal("RowVersion")));
}
