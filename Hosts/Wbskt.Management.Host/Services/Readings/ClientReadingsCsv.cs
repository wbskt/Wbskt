using System.Globalization;
using System.Text;
using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Services.Readings;

/// <summary>Writes readings as CSV for a spreadsheet: one row per reading, times in UTC.</summary>
public static class ClientReadingsCsv
{
    public const string Header = "name,deviceTime,receivedAt,value,late";

    public static string Write(IEnumerable<ClientReading> readings)
    {
        var csv = new StringBuilder(Header).Append("\r\n");
        foreach (var reading in readings)
        {
            csv.Append(Field(reading.Name)).Append(',')
                .Append(Time(reading.DeviceTime)).Append(',')
                .Append(Time(reading.ReceivedAt)).Append(',')
                .Append(reading.Value.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(reading.IsLate ? "true" : "false")
                .Append("\r\n");
        }

        return csv.ToString();
    }

    private static string Time(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    // Names come from devices. One starting like a formula would run as one when the file is opened
    // in a spreadsheet, so it is prefixed with a quote mark, which the spreadsheet shows as text.
    internal static string Field(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }
}
