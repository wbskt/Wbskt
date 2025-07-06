using System.Data;
using Wbskt.Common.Records;

namespace Wbskt.Common.Extensions;

public static class ProviderExtensions
{
    public static object? ReplaceDbNulls(object? value)
    {
        if (value is DateTime time && time == DateTime.MinValue)
        {
            return DBNull.Value;
        }

        if (value == null)
        {
            return DBNull.Value;
        }

        if (value == DBNull.Value)
        {
            return null;
        }

        return value;
    }

    public static DataTable PublisherChannelPairsToDataTable(IEnumerable<PublisherChannelRecord> pairs)
    {
        var dataTable = new DataTable();

        dataTable.Columns.Add(new DataColumn("PublisherId", typeof(int)) { AllowDBNull = false });
        dataTable.Columns.Add(new DataColumn("ChannelId", typeof(int)) { AllowDBNull = false });

        foreach (var pair in pairs)
        {
            var row = dataTable.NewRow();
            row["PublisherId"] = pair.PublisherId;
            row["ChannelId"] = pair.ChannelId;
            dataTable.Rows.Add(row);
        }

        return dataTable;
    }

    public static DataTable ClientChannelPairsToDataTable(IEnumerable<ClientChannelRecord> pairs)
    {
        var dataTable = new DataTable();

        dataTable.Columns.Add(new DataColumn("ClientId", typeof(int)) { AllowDBNull = false });
        dataTable.Columns.Add(new DataColumn("ChannelId", typeof(int)) { AllowDBNull = false });

        foreach (var pair in pairs)
        {
            var row = dataTable.NewRow();
            row["ClientId"] = pair.ClientId;
            row["ChannelId"] = pair.ChannelId;
            dataTable.Rows.Add(row);
        }

        return dataTable;
    }
}
