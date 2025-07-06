using System.Data;
using System.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Providers;
using Wbskt.Common.Records;

namespace Wbskt.Common.Writers.Implementations;

internal sealed class PublishersChannelsWriter(ILogger<PublishersChannelsWriter> logger, IConnectionStringProvider connectionStringProvider) : IPublishersChannelsWriter
{
    public void UpsertPublisherChannel(int publisherId, int channelId)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(UpsertPublisherChannel));
        ArgumentOutOfRangeException.ThrowIfLessThan(publisherId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channelId, 1);

        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.PublishersChannels_Upsert";

        command.Parameters.Add(new SqlParameter("@PublisherId", publisherId));
        command.Parameters.Add(new SqlParameter("@ChannelId", channelId));

        command.ExecuteNonQuery();
    }

    public void DeletePublisherChannel(int publisherId, int channelId)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(DeletePublisherChannel));
        ArgumentOutOfRangeException.ThrowIfLessThan(publisherId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channelId, 1);

        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.PublishersChannels_Delete";

        command.Parameters.Add(new SqlParameter("@PublisherId", publisherId));
        command.Parameters.Add(new SqlParameter("@ChannelId", channelId));

        var rowsAffected = command.ExecuteNonQuery();
        if (rowsAffected == 0)
        {
            logger.LogWarning("no publisher-channel relation found to delete: PublisherId={PublisherId}, ChannelId={ChannelId}", publisherId, channelId);
        }
    }

    public void BulkUpsertPublisherChannels(IReadOnlyCollection<PublisherChannelRecord> pairs)
    {
        logger.LogTrace("DB operation: {functionName} with {count} pairs", nameof(BulkUpsertPublisherChannels), pairs.Count);
        ArgumentNullException.ThrowIfNull(pairs);

        if (pairs.Count == 0)
        {
            logger.LogDebug("No publisher-channel pairs provided for bulk upsert");
            return;
        }

        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.PublishersChannels_BulkUpsert";

        var dataTable = new DataTable();
        dataTable.Columns.Add("PublisherId", typeof(int));
        dataTable.Columns.Add("ChannelId", typeof(int));

        foreach (var pair in pairs)
        {
            dataTable.Rows.Add(pair.PublisherId, pair.ChannelId);
        }

        var parameter = command.Parameters.AddWithValue("@PublisherChannelData", dataTable);
        parameter.SqlDbType = SqlDbType.Structured;
        parameter.TypeName = "dbo.IdIntValueTableType";

        command.ExecuteNonQuery();
        logger.LogDebug("Successfully bulk upserted {count} publisher-channel relations", pairs.Count);
    }

    public void BulkDeletePublisherChannels(IReadOnlyCollection<PublisherChannelRecord> pairs)
    {
        logger.LogTrace("DB operation: {functionName} with {count} pairs", nameof(BulkDeletePublisherChannels), pairs.Count);
        ArgumentNullException.ThrowIfNull(pairs);

        if (pairs.Count == 0)
        {
            logger.LogDebug("No publisher-channel pairs provided for bulk delete");
            return;
        }

        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.PublishersChannels_BulkDelete";

        var dataTable = new DataTable();
        dataTable.Columns.Add("PublisherId", typeof(int));
        dataTable.Columns.Add("ChannelId", typeof(int));

        foreach (var pair in pairs)
        {
            dataTable.Rows.Add(pair.PublisherId, pair.ChannelId);
        }

        var parameter = command.Parameters.AddWithValue("@PublisherChannelData", dataTable);
        parameter.SqlDbType = SqlDbType.Structured;
        parameter.TypeName = "dbo.IdIntValueTableType";

        var rowsAffected = command.ExecuteNonQuery();
        logger.LogDebug("Successfully bulk deleted {count} publisher-channel relations, {rowsAffected} rows affected", pairs.Count, rowsAffected);
    }
}
