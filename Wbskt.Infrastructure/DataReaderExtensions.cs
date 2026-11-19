using System.Data.Common;
using Wbskt.Models;

namespace Wbskt.Infrastructure;

/// <summary>
/// Column reads by name. A <c>DATETIME2</c> comes back with <see cref="DateTimeKind.Unspecified"/> but
/// holds UTC, so the time readers mark it UTC; a provider never converts one by hand.
/// </summary>
public static class DataReaderExtensions
{
    public static DateTime GetUtcDateTime(this DbDataReader reader, string column) =>
        reader.GetDateTime(reader.GetOrdinal(column)).AsUtc();

    public static DateTime? GetNullableUtcDateTime(this DbDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal).AsUtc();
    }

    public static DateTimeOffset GetUtcDateTimeOffset(this DbDataReader reader, string column) =>
        reader.GetUtcDateTime(column).ToUtcOffset();

    public static DateTimeOffset? GetNullableUtcDateTimeOffset(this DbDataReader reader, string column) =>
        reader.GetNullableUtcDateTime(column)?.ToUtcOffset();

    public static string? GetNullableString(this DbDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public static int? GetNullableInt32(this DbDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    public static Guid? GetNullableGuid(this DbDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }
}
