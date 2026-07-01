using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

internal sealed class BookmarkProvider : BaseSqlProvider, IBookmarkProvider
{
    private readonly ILogger<BookmarkProvider>? _logger;

    public BookmarkProvider(IConfiguration configuration, ILogger<BookmarkProvider>? logger = null) 
        : base(configuration)
    {
        _logger = logger;
    }

    public async Task<BookmarkRow> CreateAsync(BookmarkRow row, CancellationToken ct)
    {
        var result = await ExecuteSingleAsync(
            "dbo.Bookmark_Create",
            p =>
            {
                p.AddWithValue("@RefId", row.RefId);
                p.AddWithValue("@RunId", row.RunId);
                p.AddWithValue("@BranchRefId", row.BranchRefId);
                p.AddWithValue("@NodeId", row.NodeId);
                p.AddWithValue("@WakeConditionKind", row.WakeConditionKind);
                p.AddWithValue("@MatchKey", row.MatchKey);
                p.AddWithValue("@WakeConditionJson", row.WakeConditionJson);
                p.AddWithValue("@ExpiresAt", (object?)row.ExpiresAt ?? DBNull.Value);
                p.AddWithValue("@TtlPort", (object?)row.TtlPort ?? DBNull.Value);
            },
            Map,
            new InvalidOperationException("Failed to create bookmark."),
            ct
        );

        _logger?.LogDebug("Created bookmark {BookmarkId} for run {RunId} on branch {BranchRefId} with match key {MatchKey}", result.Id, result.RunId, result.BranchRefId, result.MatchKey);
        return result;
    }

    public async Task<BookmarkRow> GetByRefIdAsync(Guid refId, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.Bookmark_GetBy_RefId",
            p => p.AddWithValue("@RefId", refId),
            Map,
            new KeyNotFoundException($"Bookmark with RefId={refId} not found."),
            ct
        );
    }

    public async Task<BookmarkRow> GetByIdAsync(long bookmarkId, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.Bookmark_GetBy_Id",
            p => p.AddWithValue("@Id", checked((int)bookmarkId)),
            Map,
            new KeyNotFoundException($"Bookmark with Id={bookmarkId} not found."),
            ct
        );
    }

    public async Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeyAsync(string matchKey, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.Bookmark_GetAllBy_MatchKey",
            p => p.AddWithValue("@MatchKey", matchKey),
            Map,
            ct
        );
    }

    public async Task<IReadOnlyCollection<BookmarkRow>> GetAllByMatchKeysAsync(IReadOnlyCollection<string> matchKeys, CancellationToken ct)
    {
        if (matchKeys == null || matchKeys.Count == 0)
        {
            return Array.Empty<BookmarkRow>();
        }

        string jsonKeys = JsonSerializer.Serialize(matchKeys);

        return await ExecuteCollectionAsync(
            "dbo.Bookmark_GetAllBy_MatchKeys",
            p => p.AddWithValue("@MatchKeysJson", jsonKeys),
            Map,
            ct
        );
    }

    public async Task<IReadOnlyCollection<BookmarkRow>> GetAllByRunIdAsync(int runId, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.Bookmark_GetAllBy_RunId",
            p => p.AddWithValue("@RunId", runId),
            Map,
            ct
        );
    }

    public async Task<IReadOnlyCollection<BookmarkRow>> LeaseDueAsync(DateTime nowUtc, int batchSize, string hostId, TimeSpan leaseDuration, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.Bookmark_GetDue",
            p =>
            {
                p.AddWithValue("@Now", nowUtc);
                p.AddWithValue("@BatchSize", batchSize);
            },
            Map,
            ct
        );
    }

    public async Task DeleteAsync(Guid refId, CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            "dbo.Bookmark_Delete",
            p => p.AddWithValue("@RefId", refId),
            ct
        );
    }

    public async Task DeleteSiblingsAsync(long runId, long branchId, long excludeBookmarkId, CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            "dbo.Bookmark_DeleteSiblings",
            p =>
            {
                p.AddWithValue("@RunId", checked((int)runId));
                p.AddWithValue("@BranchId", checked((int)branchId));
                p.AddWithValue("@ExcludeId", checked((int)excludeBookmarkId));
            },
            ct
        );
    }

    public async Task<long> CountAsync(CancellationToken ct)
    {
        var result = await ExecuteScalarAsync<object>(
            "dbo.Bookmark_Count",
            null,
            ct
        );
        return result is long count ? count : Convert.ToInt64(result ?? 0L);
    }

    public async Task<int> DeleteOrphansAsync(CancellationToken ct)
    {
        return await ExecuteNonQueryResultAsync(
            "dbo.Bookmark_DeleteOrphans",
            null,
            ct
        );
    }

    public async Task DeleteAllByRunIdAsync(int runId, CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            "dbo.Bookmark_DeleteAllBy_RunId",
            p => p.AddWithValue("@RunId", runId),
            ct
        );
    }

    public async Task<IReadOnlyDictionary<string, long>> CountGroupedByWakeKindAsync(CancellationToken ct)
    {
        var results = await ExecuteCollectionAsync(
            "dbo.Bookmark_CountGroupedByWakeKind",
            null,
            r => new
            {
                Kind = r.GetString(r.GetOrdinal("WakeConditionKind")),
                Count = r.GetValue(r.GetOrdinal("Count"))
            },
            ct
        );

        return results.ToDictionary(
            x => x.Kind,
            x => x.Count is int i ? (long)i : Convert.ToInt64(x.Count),
            StringComparer.OrdinalIgnoreCase
        );
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
