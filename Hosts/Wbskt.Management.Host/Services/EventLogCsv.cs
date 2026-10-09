using System.Globalization;
using System.Text;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Services.Readings;

namespace Wbskt.Management.Host.Services;

/// <summary>Writes event log entries as CSV for a spreadsheet: one row per entry, newest first, times in UTC.</summary>
public static class EventLogCsv
{
    public const string Header = "id,createdAt,event,criticality,userRefId,clientRefId,policyRefId,workflowRefId,data";

    public static string Write(IEnumerable<EventLogResponse> entries)
    {
        var csv = new StringBuilder(Header).Append("\r\n");
        foreach (var entry in entries)
        {
            csv.Append(entry.Id.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(entry.CreatedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)).Append(',')
                .Append(ClientReadingsCsv.Field(entry.EventName)).Append(',')
                .Append(entry.Criticality.ToString()).Append(',')
                .Append(entry.UserRefId?.ToString()).Append(',')
                .Append(entry.ClientRefId?.ToString()).Append(',')
                .Append(entry.PolicyRefId?.ToString()).Append(',')
                .Append(entry.WorkflowRefId?.ToString()).Append(',')
                // The data holds names devices and people chose, so it is guarded like any other field.
                .Append(ClientReadingsCsv.Field(entry.EventData))
                .Append("\r\n");
        }

        return csv.ToString();
    }
}
