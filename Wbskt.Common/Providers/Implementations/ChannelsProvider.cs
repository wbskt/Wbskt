using System.Data;
using System.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Contracts;
using Wbskt.Common.Extensions;

namespace Wbskt.Common.Providers.Implementations;

internal sealed class ChannelsProvider(ILogger<ChannelsProvider> logger, IConnectionStringProvider connectionStringProvider) : IChannelsProvider
{
    private readonly ILogger<ChannelsProvider> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public IReadOnlyCollection<ChannelDetails> GetAllByChannelPublisherId(Guid channelPublisherId)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(GetAllByChannelPublisherId));
        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.Channels_GetAll_ChannelPublisherId";

        command.Parameters.Add(new SqlParameter("@ChannelPublisherId", channelPublisherId));

        var result = new List<ChannelDetails>();
        using var reader = command.ExecuteReader();
        var mapping = GetColumnMapping(reader);

        while (reader.Read())
        {
            result.Add(ParseData(reader, mapping));
        }

        return result;
    }

    public IReadOnlyCollection<ChannelDetails> GetAllByChannelUserId(int userId)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(GetAllByChannelUserId));
        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.Channels_GetAll_UserId";

        command.Parameters.Add(new SqlParameter("@UserId", userId));

        var result = new List<ChannelDetails>();
        using var reader = command.ExecuteReader();
        var mapping = GetColumnMapping(reader);

        while (reader.Read())
        {
            result.Add(ParseData(reader, mapping));
        }

        return result;
    }

    public IReadOnlyCollection<ChannelDetails> GetAll()
    {
        logger.LogTrace("DB operation: {functionName}", nameof(GetAll));
        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.Channels_GetAll";

        var result = new List<ChannelDetails>();
        using var reader = command.ExecuteReader();
        var mapping = GetColumnMapping(reader);

        while (reader.Read())
        {
            result.Add(ParseData(reader, mapping));
        }

        return result;
    }

    public ChannelDetails? GetByChannelSubscriberId(Guid channelSubscriberId)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(GetByChannelSubscriberId));
        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.Channels_GetBy_ChannelSubscriberId";

        command.Parameters.Add(new SqlParameter("@ChannelSubscriberId", channelSubscriberId));

        using var reader = command.ExecuteReader();
        var mapping = GetColumnMapping(reader);
        reader.Read();
        if (reader.HasRows)
        {
            return ParseData(reader, mapping);
        }

        return null;
    }

    public ChannelDetails? GetByChannelId(int channelId)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(GetByChannelId));
        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.Channels_GetBy_Id";

        command.Parameters.Add(new SqlParameter("@Id", channelId));

        using var reader = command.ExecuteReader();
        var mapping = GetColumnMapping(reader);
        reader.Read();
        if (reader.HasRows)
        {
            return ParseData(reader, mapping);
        }

        return null;
    }

    public int CreateChannel(ChannelDetails channel)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(CreateChannel));
        ArgumentNullException.ThrowIfNull(channel);

        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.Channels_Insert";

        command.Parameters.Add(new SqlParameter("@UserId", ProviderExtensions.ReplaceDbNulls(channel.UserId)));
        command.Parameters.Add(new SqlParameter("@ChannelName", ProviderExtensions.ReplaceDbNulls(channel.ChannelName)));
        command.Parameters.Add(new SqlParameter("@ChannelSecret", ProviderExtensions.ReplaceDbNulls(channel.ChannelSecret)));
        command.Parameters.Add(new SqlParameter("@ChannelPublisherId", ProviderExtensions.ReplaceDbNulls(channel.ChannelPublisherId)));
        command.Parameters.Add(new SqlParameter("@ChannelSubscriberId", ProviderExtensions.ReplaceDbNulls(channel.ChannelSubscriberId)));

        var id = new SqlParameter("@Id", SqlDbType.Int) { Size = int.MaxValue };
        id.Direction = ParameterDirection.Output;
        command.Parameters.Add(id);

        command.ExecuteNonQuery();

        return channel.ChannelId = (int)(ProviderExtensions.ReplaceDbNulls(id.Value) ?? 0);
    }

    private static ChannelDetails ParseData(SqlDataReader reader, OrdinalColumnMapping mapping)
    {
        var data = new ChannelDetails
        {
            UserId = reader.GetInt32(mapping.UserId),
            ChannelId = reader.GetInt32(mapping.ChannelId),
            ChannelName = reader.GetString(mapping.ChannelName),
            ChannelSecret = reader.GetString(mapping.ChannelSecret),
            ChannelPublisherId = reader.GetGuid(mapping.ChannelPublisherId),
            ChannelSubscriberId = reader.GetGuid(mapping.ChannelSubscriberId)
        };

        return data;
    }

    private static OrdinalColumnMapping GetColumnMapping(SqlDataReader reader)
    {
        var mapping = new OrdinalColumnMapping();

        mapping.UserId = reader.GetOrdinal("UserId");
        mapping.ChannelId = reader.GetOrdinal("Id");
        mapping.ChannelName = reader.GetOrdinal("ChannelName");
        mapping.ChannelSecret = reader.GetOrdinal("ChannelSecret");
        mapping.ChannelPublisherId = reader.GetOrdinal("ChannelPublisherId");
        mapping.ChannelSubscriberId = reader.GetOrdinal("ChannelSubscriberId");

        return mapping;
    }

    private class OrdinalColumnMapping
    {
        public int UserId;
        public int ChannelId;
        public int ChannelName;
        public int ChannelSecret;
        public int ChannelPublisherId;
        public int ChannelSubscriberId;
    }
}
