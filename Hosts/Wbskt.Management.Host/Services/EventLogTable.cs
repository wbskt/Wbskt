using System.Data;
using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Services;

/// <summary>
/// The table-valued parameter for dbo.EventLogs_InsertBatch. SQL Server matches its columns to
/// dbo.EventLogTableType by position, not name, so the order here must follow the type exactly.
/// </summary>
public static class EventLogTable
{
    public static DataTable Build(IReadOnlyCollection<EventLogEntry> batch)
    {
        var dt = new DataTable();
        dt.Columns.Add("EventId",       typeof(int));
        dt.Columns.Add("EventData",     typeof(string));
        dt.Columns.Add("CreatedAtUtc",  typeof(DateTime));
        dt.Columns.Add("WorkspaceId",   typeof(int));
        dt.Columns.Add("PolicyId",      typeof(int));
        dt.Columns.Add("ClientId",      typeof(int));      // moved up
        dt.Columns.Add("WorkflowId",    typeof(int));      // moved up
        dt.Columns.Add("PolicyRefId",   typeof(Guid));     // moved down
        dt.Columns.Add("ClientRefId",   typeof(Guid));     // moved down
        dt.Columns.Add("WorkflowRefId", typeof(Guid));
        dt.Columns.Add("UserId",        typeof(int));
        dt.Columns.Add("UserRefId",     typeof(Guid));
        dt.Columns.Add("MessageId",     typeof(Guid));
        dt.Columns.Add("Source",        typeof(byte));
        dt.Columns.Add("ClientAddress", typeof(string));
        dt.Columns.Add("UserAgent",     typeof(string));

        foreach (var item in batch)
        {
            dt.Rows.Add(
                item.EventId,
                item.EventData,
                item.CreatedAtUtc,
                (object?)item.WorkspaceId  ?? DBNull.Value,
                (object?)item.PolicyId     ?? DBNull.Value,
                item.ClientId > 0 ? item.ClientId : DBNull.Value,   // matches new order
                (object?)item.WorkflowId   ?? DBNull.Value,         // matches new order
                (object?)item.PolicyRefId  ?? DBNull.Value,
                (object?)item.ClientRefId  ?? DBNull.Value,
                (object?)item.WorkflowRefId ?? DBNull.Value,
                (object?)item.UserId       ?? DBNull.Value,
                (object?)item.UserRefId    ?? DBNull.Value,
                (object?)item.MessageId    ?? DBNull.Value,
                item.Source is { } source ? (byte)source : DBNull.Value,
                (object?)item.ClientAddress ?? DBNull.Value,
                (object?)item.UserAgent    ?? DBNull.Value);
        }

        return dt;
    }
}
