using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

internal sealed class BranchProvider : BaseSqlProvider, IBranchProvider
{
    public BranchProvider(IConfiguration configuration) : base(configuration) { }

    public Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct)
    {
        return UpsertAsync(row, ct);
    }

    public async Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.Branch_Upsert",
            p =>
            {
                p.AddWithValue("@RefId", row.RefId);
                p.AddWithValue("@RunId", row.RunId);
                p.AddWithValue("@ParentBranchId", (object?)row.ParentBranchId ?? DBNull.Value);
                p.AddWithValue("@ForkCohortId", (object?)row.ForkCohortId ?? DBNull.Value);
                p.AddWithValue("@NodeId", row.NodeId);
                p.AddWithValue("@Status", row.Status);
                p.AddWithValue("@PendingTakePort", (object?)row.PendingTakePort ?? DBNull.Value);
                p.AddWithValue("@LocalJson", row.LocalJson);
                p.AddWithValue("@LastOutputJson", (object?)row.LastOutputJson ?? DBNull.Value);
                p.AddWithValue("@CompensationStackJson", (object?)row.CompensationStackJson ?? DBNull.Value);
            },
            Map,
            new InvalidOperationException("Branch_Upsert did not return a row."),
            ct
        );
    }

    public async Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.Branch_GetById",
            p => p.AddWithValue("@Id", checked((int)branchId)),
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

    public async Task<IReadOnlyCollection<BranchRow>> GetActiveByRunIdAsync(int runId, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.Branch_GetActiveBy_RunId",
            p => p.AddWithValue("@RunId", runId),
            Map,
            ct
        );
    }

    public async Task<IReadOnlyCollection<BranchRow>> GetAllActiveAsync(CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.Branch_GetAllActive",
            null,
            Map,
            ct
        );
    }

    public async Task<IReadOnlyCollection<BranchRow>> GetRunningBranchesAsync(CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.Branch_GetRunning",
            null,
            Map,
            ct
        );
    }

    public Task<BranchRow> UpdatePointerAsync(long branchId, Guid currentNodeId, string status, string localJson, string? lastOutputJson, CancellationToken ct)
    {
        return UpdateBranchAsync(branchId, currentNodeId, status, localJson, lastOutputJson, ct);
    }

    public Task<BranchRow> SetCompletedAsync(long branchId, CancellationToken ct)
    {
        return UpdateBranchAsync(branchId, null, "Completed", null, null, ct);
    }

    public Task<BranchRow> SetFailedAsync(long branchId, string? lastOutputJson, CancellationToken ct)
    {
        return UpdateBranchAsync(branchId, null, "Failed", null, lastOutputJson, ct);
    }

    private async Task<BranchRow> UpdateBranchAsync(long branchId, Guid? currentNodeId, string status, string? localJson, string? lastOutputJson, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.Branch_Update",
            p =>
            {
                p.AddWithValue("@Id", checked((int)branchId));
                p.AddWithValue("@NodeId", (object?)currentNodeId ?? DBNull.Value);
                p.AddWithValue("@Status", status);
                p.AddWithValue("@LocalJson", (object?)localJson ?? DBNull.Value);
                p.AddWithValue("@LastOutputJson", (object?)lastOutputJson ?? DBNull.Value);
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

    internal static BranchRow Map(DbDataReader reader)
    {
        return new BranchRow
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            RefId = reader.GetGuid(reader.GetOrdinal("RefId")),
            RunId = reader.GetInt32(reader.GetOrdinal("RunId")),
            ParentBranchId = reader.IsDBNull(reader.GetOrdinal("ParentBranchId")) ? null : reader.GetGuid(reader.GetOrdinal("ParentBranchId")),
            ForkCohortId = reader.IsDBNull(reader.GetOrdinal("ForkCohortId")) ? null : reader.GetGuid(reader.GetOrdinal("ForkCohortId")),
            NodeId = reader.GetGuid(reader.GetOrdinal("NodeId")),
            Status = reader.GetString(reader.GetOrdinal("Status")),
            PendingTakePort = reader.IsDBNull(reader.GetOrdinal("PendingTakePort")) ? null : reader.GetString(reader.GetOrdinal("PendingTakePort")),
            LocalJson = reader.GetString(reader.GetOrdinal("LocalJson")),
            LastOutputJson = reader.IsDBNull(reader.GetOrdinal("LastOutputJson")) ? null : reader.GetString(reader.GetOrdinal("LastOutputJson")),
            CompensationStackJson = reader.IsDBNull(reader.GetOrdinal("CompensationStackJson")) ? null : reader.GetString(reader.GetOrdinal("CompensationStackJson")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
            RowVersion = (byte[])reader.GetValue(reader.GetOrdinal("RowVersion"))
        };
    }
}
