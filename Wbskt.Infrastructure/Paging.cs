namespace Wbskt.Infrastructure;

/// <summary>
/// Bounds for the page parameters list endpoints take from the query string. Without them a caller
/// could ask for a page of millions of rows, overflow a "one more row" probe, or send a negative
/// offset straight to SQL.
/// </summary>
public static class Paging
{
    /// <summary>The largest page any list endpoint returns.</summary>
    public const int MaxPageSize = 200;

    /// <summary>A page size between 1 and <see cref="MaxPageSize"/>.</summary>
    public static int Take(int take) => Math.Clamp(take, 1, MaxPageSize);

    /// <summary>An offset of at least zero.</summary>
    public static int Skip(int skip) => Math.Max(0, skip);
}
