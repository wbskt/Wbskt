using System.Text;
using Wbskt.Infrastructure.Csv;

namespace Wbskt.Host.Tests.Infrastructure;

/// <summary>
/// An export is written a row at a time and says when it was cut (#1001), rather than building the file
/// in memory and stopping at its limit without a word.
/// </summary>
public sealed class CsvFileResultTests
{
    [Fact]
    public async Task Rows_follow_the_header_one_per_line()
    {
        var text = await WriteAsync(new CsvExport("a,b", Rows("1,2", "3,4"), MaxRows: 10));

        Assert.Equal("a,b\r\n1,2\r\n3,4\r\n", text);
    }

    [Fact]
    public async Task A_file_cut_at_its_limit_ends_by_saying_so()
    {
        var text = await WriteAsync(new CsvExport("a", Rows("1", "2", "3"), MaxRows: 2));

        Assert.Equal("a\r\n1\r\n2\r\n# truncated at 2 rows; export a shorter range\r\n", text);
    }

    [Fact]
    public async Task A_file_exactly_at_its_limit_is_not_marked_cut()
    {
        var text = await WriteAsync(new CsvExport("a", Rows("1", "2"), MaxRows: 2));

        Assert.DoesNotContain(CsvFileResult.TruncatedPrefix, text);
    }

    [Fact]
    public async Task Rows_past_the_limit_are_not_read()
    {
        int read = 0;
        async IAsyncEnumerable<string> Counting()
        {
            for (int i = 0; i < 1_000; i++)
            {
                read++;
                yield return i.ToString();
                await Task.Yield();
            }
        }

        await WriteAsync(new CsvExport("a", Counting(), MaxRows: 5));

        Assert.Equal(6, read);
    }

    private static async Task<string> WriteAsync(CsvExport export)
    {
        using var body = new MemoryStream();
        await CsvFileResult.WriteAsync(body, export, CancellationToken.None);
        return Encoding.UTF8.GetString(body.ToArray());
    }

    private static IAsyncEnumerable<string> Rows(params string[] rows) => rows.ToAsyncEnumerable();
}
