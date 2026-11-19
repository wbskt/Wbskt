using System.Data;
using Wbskt.Infrastructure;

namespace Wbskt.Host.Tests.Infrastructure;

/// <summary>
/// A DATETIME2 comes back from SQL with no kind but holds UTC; the readers say so, and a NULL is null.
/// </summary>
public sealed class DataReaderExtensionsTests
{
    private static readonly DateTime Stored = new(2026, 11, 19, 8, 30, 0, DateTimeKind.Unspecified);
    private static readonly Guid Ref = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static DataTableReader Row(bool nulls)
    {
        var table = new DataTable();
        table.Columns.Add("At", typeof(DateTime));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Count", typeof(int));
        table.Columns.Add("RefId", typeof(Guid));
        table.Rows.Add(nulls ? DBNull.Value : Stored, nulls ? DBNull.Value : "a", nulls ? DBNull.Value : 3, nulls ? DBNull.Value : Ref);
        var reader = table.CreateDataReader();
        reader.Read();
        return reader;
    }

    [Fact]
    public void A_stored_time_is_read_as_utc()
    {
        using var reader = Row(nulls: false);

        Assert.Equal(DateTimeKind.Utc, reader.GetUtcDateTime("At").Kind);
        Assert.Equal(Stored.Ticks, reader.GetUtcDateTime("At").Ticks);
        Assert.Equal(new DateTimeOffset(2026, 11, 19, 8, 30, 0, TimeSpan.Zero), reader.GetUtcDateTimeOffset("At"));
        Assert.Equal(new DateTimeOffset(2026, 11, 19, 8, 30, 0, TimeSpan.Zero), reader.GetNullableUtcDateTimeOffset("At"));
        Assert.Equal("a", reader.GetNullableString("Name"));
        Assert.Equal(3, reader.GetNullableInt32("Count"));
        Assert.Equal(Ref, reader.GetNullableGuid("RefId"));
    }

    [Fact]
    public void A_null_column_is_null()
    {
        using var reader = Row(nulls: true);

        Assert.Null(reader.GetNullableUtcDateTime("At"));
        Assert.Null(reader.GetNullableUtcDateTimeOffset("At"));
        Assert.Null(reader.GetNullableString("Name"));
        Assert.Null(reader.GetNullableInt32("Count"));
        Assert.Null(reader.GetNullableGuid("RefId"));
    }
}
