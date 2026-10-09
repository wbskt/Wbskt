using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Models;

namespace Wbskt.Infrastructure;

/// <summary>
/// The paging half of every list endpoint's query string: <c>cursor</c> (the previous page's
/// <c>nextCursor</c>, left out for the first page) and <c>limit</c>. Bind it with
/// <c>[FromQuery] PageRequest page</c>.
/// </summary>
/// <remarks>
/// The parameters lists took before this contract (<c>skip</c>, <c>take</c>, <c>top</c> and
/// <c>fromEventId</c>) are still read for one release, so callers can move over first; when both are
/// sent, <c>cursor</c> and <c>limit</c> win.
/// </remarks>
public sealed class PageRequest
{
    /// <summary>The <c>nextCursor</c> of the previous page; leave out for the first page.</summary>
    [FromQuery(Name = "cursor")]
    public string? Cursor { get; set; }

    /// <summary>Page size, 1 to 200. Each list has its own default.</summary>
    [FromQuery(Name = "limit")]
    public int? Limit { get; set; }

    /// <summary>Deprecated: use <c>cursor</c>.</summary>
    [FromQuery(Name = "skip")]
    public int? Skip { get; set; }

    /// <summary>Deprecated: use <c>limit</c>.</summary>
    [FromQuery(Name = "take")]
    public int? Take { get; set; }

    /// <summary>Deprecated: use <c>limit</c>.</summary>
    [FromQuery(Name = "top")]
    public int? Top { get; set; }

    /// <summary>Deprecated: use <c>cursor</c>.</summary>
    [FromQuery(Name = "fromEventId")]
    public long? FromEventId { get; set; }

    public static readonly Error CursorInvalid = Error.Validation("PAGE_CURSOR_INVALID", "'cursor' must be the nextCursor of a previous page of this list.");

    /// <summary>The page size asked for, or <paramref name="defaultLimit"/>, clamped to 1..<see cref="Paging.MaxPageSize"/>.</summary>
    public int LimitOr(int defaultLimit) => Paging.Take(Limit ?? Take ?? Top ?? defaultLimit);

    /// <summary>
    /// The key a keyset list reads after, or null for the first page. A list ordered by an increasing
    /// or decreasing id hands the last id of each page out as its cursor.
    /// </summary>
    public Result<long?> AfterKey()
    {
        if (Cursor is null)
        {
            return Result<long?>.Success(FromEventId);
        }

        return long.TryParse(Cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var key)
            ? Result<long?>.Success(key)
            : Result<long?>.Failure(CursorInvalid);
    }

    /// <summary>The row an offset list starts at: from the cursor, else the legacy <c>skip</c>, else 0.</summary>
    public Result<int> Offset()
    {
        if (Cursor is null)
        {
            return Result<int>.Success(Paging.Skip(Skip ?? 0));
        }

        return int.TryParse(Cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var offset)
            ? Result<int>.Success(offset)
            : Result<int>.Failure(CursorInvalid);
    }

    /// <summary>The cursor for a keyset list's next page, from the last key of a page that has one after it.</summary>
    public static string? KeyCursor(long? key) => key?.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// An offset list's page: its items, the whole list's count, and a cursor while rows remain past
    /// this page.
    /// </summary>
    public static Page<T> OffsetPage<T>(IPagedList<T> rows, int offset)
    {
        var items = rows.ToList();
        var next = offset + items.Count;
        return new Page<T>
        {
            Items = items,
            TotalCount = rows.TotalCount,
            NextCursor = items.Count > 0 && next < rows.TotalCount ? next.ToString(CultureInfo.InvariantCulture) : null
        };
    }
}
