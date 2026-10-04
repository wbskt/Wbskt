using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Services.Readings;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class ClientReadingsCsvTests
{
    [Fact]
    public void Readings_are_written_one_per_row_with_utc_times()
    {
        var at = new DateTime(2026, 10, 4, 6, 7, 8, 9, DateTimeKind.Utc);

        var csv = ClientReadingsCsv.Write([
            new ClientReading("temp", at, at.AddMinutes(20), 4.25, true),
            new ClientReading("battery", at, at, 87, false)
        ]);

        Assert.Equal(
            "name,deviceTime,receivedAt,value,late\r\n" +
            "temp,2026-10-04T06:07:08.009Z,2026-10-04T06:27:08.009Z,4.25,true\r\n" +
            "battery,2026-10-04T06:07:08.009Z,2026-10-04T06:07:08.009Z,87,false\r\n",
            csv);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("=HYPERLINK(1)", "'=HYPERLINK(1)")]
    [InlineData("+1", "'+1")]
    [InlineData("-1", "'-1")]
    [InlineData("@sum", "'@sum")]
    [InlineData("=a,b", "\"'=a,b\"")]
    public void Names_are_escaped_and_cannot_run_as_formulas(string name, string field)
    {
        Assert.Equal(field, ClientReadingsCsv.Field(name));
    }
}
