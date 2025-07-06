using System.Data;
using System.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Extensions;
using Wbskt.Common.Readers;
using Wbskt.Common.Records;

namespace Wbskt.Common.Writers.Implementations;

internal sealed class ClientsChannelsWriter(ILogger<ClientsChannelsWriter> logger, IConnectionStringProvider connectionStringProvider) : IClientsChannelsWriter
{
    public void UpsertClientChannel(int clientId, int channelId)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(UpsertClientChannel));
        ArgumentOutOfRangeException.ThrowIfLessThan(clientId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channelId, 1);

        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.ClientsChannels_Add";

        command.Parameters.Add(new SqlParameter("@ClientId", clientId));
        command.Parameters.Add(new SqlParameter("@ChannelId", channelId));

        command.ExecuteNonQuery();
    }

    public void DeleteClientChannel(int clientId, int channelId)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(DeleteClientChannel));
        ArgumentOutOfRangeException.ThrowIfLessThan(clientId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channelId, 1);

        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.ClientsChannels_Remove";

        command.Parameters.Add(new SqlParameter("@ClientId", clientId));
        command.Parameters.Add(new SqlParameter("@ChannelId", channelId));

        var rowsAffected = command.ExecuteNonQuery();
        if (rowsAffected == 0)
        {
            logger.LogWarning("no client-channel relation found to delete: ClientId={ClientId}, ChannelId={ChannelId}", clientId, channelId);
        }
    }

    public void BulkUpsertClientChannels(IReadOnlyCollection<ClientChannelRecord> pairs)
    {
        logger.LogTrace("DB operation: {functionName} with {count} pairs", nameof(BulkUpsertClientChannels), pairs.Count);
        ArgumentNullException.ThrowIfNull(pairs);

        if (pairs.Count == 0)
        {
            logger.LogDebug("no client-channel pairs provided for bulk upsert");
            return;
        }

        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.ClientsChannels_BulkUpsert";

        var dataTable = ProviderExtensions.ClientChannelPairsToDataTable(pairs);

        var parameter = command.Parameters.AddWithValue("@ClientChannelData", dataTable);
        parameter.SqlDbType = SqlDbType.Structured;
        parameter.TypeName = "dbo.ClientChannelTableType";

        command.ExecuteNonQuery();
        logger.LogDebug("successfully bulk upserted {count} client-channel relations", pairs.Count);
    }

    public void BulkDeleteClientChannels(IReadOnlyCollection<ClientChannelRecord> pairs)
    {
        logger.LogTrace("DB operation: {functionName} with {count} pairs", nameof(BulkDeleteClientChannels), pairs.Count);
        ArgumentNullException.ThrowIfNull(pairs);

        if (pairs.Count == 0)
        {
            logger.LogDebug("no client-channel pairs provided for bulk delete");
            return;
        }

        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.ClientsChannels_BulkDelete";

        var dataTable = ProviderExtensions.ClientChannelPairsToDataTable(pairs);

        var parameter = command.Parameters.AddWithValue("@ClientChannelData", dataTable);
        parameter.SqlDbType = SqlDbType.Structured;
        parameter.TypeName = "dbo.ClientChannelTableType";

        var rowsAffected = command.ExecuteNonQuery();
        logger.LogDebug("successfully bulk deleted {count} client-channel relations, {rowsAffected} rows affected", pairs.Count, rowsAffected);
    }
}
