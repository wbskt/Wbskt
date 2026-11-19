using System.Globalization;
using System.Text;
using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Services.Readings;

/// <summary>
/// Writes readings as CSV for a spreadsheet, times in UTC: one row per reading, or per variable and
/// hour for the hourly export.
/// </summary>
public static class ClientReadingsCsv
{
    public const string Header = "name,sentAt,receivedAt,value,late";
    public const string HourlyHeader = "name,hourStart,count,min,avg,max";

    public static string Write(IEnumerable<ClientReading> readings)
    {
        var csv = new StringBuilder(Header).Append("\r\n");
        foreach (var reading in readings)
        {
            csv.Append(Field(reading.Name)).Append(',')
                .Append(Time(reading.SentAt)).Append(',')
                .Append(Time(reading.ReceivedAt)).Append(',')
                .Append(reading.Value.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(reading.IsLate ? "true" : "false")
                .Append("\r\n");
        }

        return csv.ToString();
    }

    public static string WriteHourly(IEnumerable<ClientReadingHour> hours)
    {
        var csv = new StringBuilder(HourlyHeader).Append("\r\n");
        foreach (var hour in hours)
        {
            csv.Append(Field(hour.Name)).Append(',')
                .Append(Time(hour.HourStart)).Append(',')
                .Append(hour.Count.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(hour.Min.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(hour.Avg.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(hour.Max.ToString("R", CultureInfo.InvariantCulture))
                .Append("\r\n");
        }

        return csv.ToString();
    }

    private static string Time(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    // Names come from clients. One starting like a formula would run as one when the file is opened
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
