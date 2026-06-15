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
    private readonly string _connectionString;

    public BranchProvider(IConfiguration configuration) : base(configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection connection string not found.");
    }

    public Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct)
    {
        return UpsertAsync(row, ct);
    }

    public async Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Branch_Upsert", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RefId", row.RefId);
        command.Parameters.AddWithValue("@RunId", row.RunId);
        command.Parameters.AddWithValue("@ParentBranchId", (object?)row.ParentBranchId ?? DBNull.Value);
        command.Parameters.AddWithValue("@ForkCohortId", (object?)row.ForkCohortId ?? DBNull.Value);
        command.Parameters.AddWithValue("@NodeId", row.NodeId);
        command.Parameters.AddWithValue("@Status", row.Status);
        command.Parameters.AddWithValue("@PendingTakePort", (object?)row.PendingTakePort ?? DBNull.Value);
        command.Parameters.AddWithValue("@LocalJson", row.LocalJson);
        command.Parameters.AddWithValue("@LastOutputJson", (object?)row.LastOutputJson ?? DBNull.Value);
        command.Parameters.AddWithValue("@CompensationStackJson", (object?)row.CompensationStackJson ?? DBNull.Value);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new InvalidOperationException("Branch_Upsert did not return a row.");
    }

    public async Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(
            """
            SELECT
                Id,
                RefId,
                RunId,
                ParentBranchId,
                ForkCohortId,
                NodeId,
                Status,
                PendingTakePort,
                LocalJson,
                LastOutputJson,
                CompensationStackJson,
                CreatedAt,
                UpdatedAt,
                RowVersion
            FROM dbo.Branches
            WHERE Id = @Id;
            """,
            connection);
        command.CommandType = CommandType.Text;
        command.Parameters.AddWithValue("@Id", checked((int)branchId));

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new KeyNotFoundException($"Branch with Id={branchId} not found.");
    }

    public async Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Branch_GetBy_RefId", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RefId", refId);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new KeyNotFoundException($"Branch with RefId={refId} not found.");
    }

    public async Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Branch_GetAllBy_RunId", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RunId", runId);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<BranchRow>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(Map(reader));
        }

        return results.AsReadOnly();
    }

    public async Task<IReadOnlyCollection<BranchRow>> GetActiveByRunIdAsync(int runId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Branch_GetActiveBy_RunId", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RunId", runId);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<BranchRow>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(Map(reader));
        }

        return results.AsReadOnly();
    }

    public async Task<IReadOnlyCollection<BranchRow>> GetAllActiveAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Branch_GetAllActive", connection);
        command.CommandType = CommandType.StoredProcedure;

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<BranchRow>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(Map(reader));
        }

        return results.AsReadOnly();
    }

    public async Task<IReadOnlyCollection<BranchRow>> GetRunningBranchesAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Branch_GetRunning", connection);
        command.CommandType = CommandType.StoredProcedure;

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<BranchRow>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(Map(reader));
        }

        return results.AsReadOnly();
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
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(
            """
            UPDATE dbo.Branches
            SET
                NodeId = COALESCE(@NodeId, NodeId),
                Status = @Status,
                LocalJson = COALESCE(@LocalJson, LocalJson),
                LastOutputJson = @LastOutputJson,
                PendingTakePort = NULL,
                UpdatedAt = SYSUTCDATETIME()
            WHERE Id = @Id;

            SELECT
                Id,
                RefId,
                RunId,
                ParentBranchId,
                ForkCohortId,
                NodeId,
                Status,
                PendingTakePort,
                LocalJson,
                LastOutputJson,
                CompensationStackJson,
                CreatedAt,
                UpdatedAt,
                RowVersion
            FROM dbo.Branches
            WHERE Id = @Id;
            """,
            connection);
        command.CommandType = CommandType.Text;
        command.Parameters.AddWithValue("@Id", checked((int)branchId));
        command.Parameters.AddWithValue("@NodeId", (object?)currentNodeId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Status", status);
        command.Parameters.AddWithValue("@LocalJson", (object?)localJson ?? DBNull.Value);
        command.Parameters.AddWithValue("@LastOutputJson", (object?)lastOutputJson ?? DBNull.Value);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new KeyNotFoundException($"Branch with Id={branchId} not found.");
    }

    public async Task<int> CancelWaitingBranchesAsync(int runId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(
            """
            UPDATE dbo.Branches
            SET Status = 'Cancelled', UpdatedAt = SYSUTCDATETIME()
            WHERE RunId = @RunId AND Status IN ('Waiting', 'WaitingAtJoin');
            """,
            connection);
        command.CommandType = CommandType.Text;
        command.Parameters.AddWithValue("@RunId", runId);

        await connection.OpenAsync(ct);
        return await command.ExecuteNonQueryAsync(ct);
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
