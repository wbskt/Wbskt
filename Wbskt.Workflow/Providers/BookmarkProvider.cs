using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

public class BookmarkProvider : BaseSqlProvider, IBookmarkProvider
{
    private readonly string _connectionString;

    public BookmarkProvider(IConfiguration configuration) : base(configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection connection string not found.");
    }

    public async Task<BookmarkRow> CreateAsync(BookmarkRow row, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Bookmark_Create", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RefId", row.RefId);
        command.Parameters.AddWithValue("@RunId", row.RunId);
        command.Parameters.AddWithValue("@BranchRefId", row.BranchRefId);
        command.Parameters.AddWithValue("@NodeId", row.NodeId);
        command.Parameters.AddWithValue("@WakeConditionKind", row.WakeConditionKind);
        command.Parameters.AddWithValue("@MatchKey", row.MatchKey);
        command.Parameters.AddWithValue("@WakeConditionJson", row.WakeConditionJson);
        command.Parameters.AddWithValue("@ExpiresAt", (object?)row.ExpiresAt ?? DBNull.Value);
        command.Parameters.AddWithValue("@TtlPort", (object?)row.TtlPort ?? DBNull.Value);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new InvalidOperationException("Bookmark_Create did not return a row.");
    }

    public async Task<BookmarkRow> GetByRefIdAsync(Guid refId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Bookmark_GetBy_RefId", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RefId", refId);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new KeyNotFoundException($"Bookmark with RefId={refId} not found.");
    }

    public async Task<BookmarkRow?> GetByIdAsync(long bookmarkId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Bookmark_GetBy_Id", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@Id", bookmarkId);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        return null;
    }

    public async Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeyAsync(string matchKey, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Bookmark_GetAllBy_MatchKey", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@MatchKey", matchKey);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<BookmarkRow>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(Map(reader));
        }

        return results.AsReadOnly();
    }

    public async Task<IReadOnlyCollection<BookmarkRow>> GetAllByRunIdAsync(int runId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Bookmark_GetAllBy_RunId", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RunId", runId);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<BookmarkRow>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(Map(reader));
        }

        return results.AsReadOnly();
    }

    public async Task<IReadOnlyCollection<BookmarkRow>> LeaseDueAsync(DateTime nowUtc, int batchSize, string hostId, TimeSpan leaseDuration, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Bookmark_GetDue", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@Now", nowUtc);
        command.Parameters.AddWithValue("@BatchSize", batchSize);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<BookmarkRow>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(Map(reader));
        }

        return results.AsReadOnly();
    }

    public async Task DeleteAsync(Guid refId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Bookmark_Delete", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RefId", refId);

        await connection.OpenAsync(ct);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteSiblingsAsync(long runId, long branchId, long excludeBookmarkId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Bookmark_DeleteSiblings", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RunId", runId);
        command.Parameters.AddWithValue("@BranchId", branchId);
        command.Parameters.AddWithValue("@ExcludeId", excludeBookmarkId);

        await connection.OpenAsync(ct);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAllByRunIdAsync(int runId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Bookmark_DeleteAllBy_RunId", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RunId", runId);

        await connection.OpenAsync(ct);
        await command.ExecuteNonQueryAsync(ct);
    }

    internal static BookmarkRow Map(DbDataReader reader)
    {
        return new BookmarkRow
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            RefId = reader.GetGuid(reader.GetOrdinal("RefId")),
            RunId = reader.GetInt32(reader.GetOrdinal("RunId")),
            BranchRefId = reader.GetGuid(reader.GetOrdinal("BranchRefId")),
            NodeId = reader.GetGuid(reader.GetOrdinal("NodeId")),
            WakeConditionKind = reader.GetString(reader.GetOrdinal("WakeConditionKind")),
            MatchKey = reader.GetString(reader.GetOrdinal("MatchKey")),
            WakeConditionJson = reader.GetString(reader.GetOrdinal("WakeConditionJson")),
            ExpiresAt = reader.IsDBNull(reader.GetOrdinal("ExpiresAt")) ? null : reader.GetDateTime(reader.GetOrdinal("ExpiresAt")),
            TtlPort = reader.IsDBNull(reader.GetOrdinal("TtlPort")) ? null : reader.GetString(reader.GetOrdinal("TtlPort")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
        };
    }
}
