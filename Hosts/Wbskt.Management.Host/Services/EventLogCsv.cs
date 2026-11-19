using System.Globalization;
using System.Text;
using Wbskt.Management.Models;
using Wbskt.Management.Host.Services.Readings;

namespace Wbskt.Management.Host.Services;

/// <summary>Writes event log entries as CSV for a spreadsheet: one row per entry, newest first, times in UTC.</summary>
public static class EventLogCsv
{
    // New columns go at the end, so a script reading the file by position keeps working.
    public const string Header = "id,createdAt,event,criticality,userRefId,clientRefId,policyRefId,workflowRefId,data,source,clientAddress,userAgent";

    public static string Row(EventLogResponse entry) =>
        new StringBuilder()
            .Append(entry.Id.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(entry.CreatedAtUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)).Append(',')
            .Append(ClientReadingsCsv.Field(entry.EventName)).Append(',')
            .Append(entry.Criticality.ToString()).Append(',')
            .Append(entry.UserRefId?.ToString()).Append(',')
            .Append(entry.ClientRefId?.ToString()).Append(',')
            .Append(entry.PolicyRefId?.ToString()).Append(',')
            .Append(entry.WorkflowRefId?.ToString()).Append(',')
            // The data, the address and the user agent hold text others chose, so each is guarded like any other field.
            .Append(ClientReadingsCsv.Field(entry.EventData)).Append(',')
            .Append(entry.Source).Append(',')
            .Append(ClientReadingsCsv.Field(entry.ClientAddress ?? string.Empty)).Append(',')
            .Append(ClientReadingsCsv.Field(entry.UserAgent ?? string.Empty))
            .ToString();
}
