using System.Data;
using System.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Contracts;
using Wbskt.Common.Extensions;

namespace Wbskt.Common.Providers.Implementations;

internal sealed class ClientProvider(ILogger<ClientProvider> logger, IConnectionStringProvider connectionStringProvider) : IClientProvider
{
    private readonly ILogger<ClientProvider> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public int FindByClientNameUserId(string clientName, int userId)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(FindByClientNameUserId));
        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.ClientConnections_FindBy_ClientName_UserId";

        command.Parameters.Add(new SqlParameter("@ClientName", clientName));
        command.Parameters.Add(new SqlParameter("@UserId", userId));

        using var reader = command.ExecuteReader();
        reader.Read();
        if (reader.HasRows)
        {
            return reader.GetInt32(reader.GetOrdinal("Id"));
        }

        return -1;
    }

    public int FindByClientUniqueId(Guid clientUniqueId)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(FindByClientUniqueId));
        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.ClientConnections_FindBy_ClientUniqueId";

        command.Parameters.Add(new SqlParameter("@ClientUniqueId", clientUniqueId));

        using var reader = command.ExecuteReader();
        reader.Read();
        if (reader.HasRows)
        {
            return reader.GetInt32(reader.GetOrdinal("Id"));
        }

        return -1;
    }

    public IReadOnlyCollection<ClientConnection> GetAll()
    {
        logger.LogTrace("DB operation: {functionName}", nameof(GetAll));
        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.ClientConnections_GetAll";

        var result = new List<ClientConnection>();
        using var reader = command.ExecuteReader();
        var mapping = GetColumnMapping(reader);

        while (reader.Read())
        {
            result.Add(ParseData(reader, mapping));
        }

        return result;
    }

    public IReadOnlyCollection<ClientConnection> GetAllByChannelId(int channelId)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(GetAllByChannelId));
        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.ClientConnections_GetAll_ChannelId";

        command.Parameters.Add(new SqlParameter("@ChannelId", channelId));

        var result = new List<ClientConnection>();
        using var reader = command.ExecuteReader();
        var mapping = GetColumnMapping(reader);

        while (reader.Read())
        {
            result.Add(ParseData(reader, mapping));
        }

        return result;
    }

    public IReadOnlyCollection<ClientConnection> GetAllByClientIds(int[] clientIds)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(GetAllByClientIds));
        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.ClientConnections_GetAll_Ids";

        var param = command.Parameters.AddWithValue("@Ids", clientIds.ToDataTable());
        param.SqlDbType = SqlDbType.Structured;
        param.TypeName = "dbo.IdListTableType";

        var result = new List<ClientConnection>();
        using var reader = command.ExecuteReader();
        var mapping = GetColumnMapping(reader);

        while (reader.Read())
        {
            result.Add(ParseData(reader, mapping));
        }

        return result;
    }

    public IReadOnlyCollection<ClientConnection> GetAllByUserId(int userId)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(GetAllByChannelId));
        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.ClientConnections_GetAll_UserId";

        command.Parameters.Add(new SqlParameter("@UserId", userId));

        var result = new List<ClientConnection>();
        using var reader = command.ExecuteReader();
        var mapping = GetColumnMapping(reader);

        while (reader.Read())
        {
            result.Add(ParseData(reader, mapping));
        }

        return result;
    }

    public ClientConnection? GetByClientId(int clientId)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(GetByClientId));
        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.ClientConnections_GetBy_Id";

        command.Parameters.Add(new SqlParameter("@Id", clientId));

        using var reader = command.ExecuteReader();
        var mapping = GetColumnMapping(reader);
        reader.Read();
        if (reader.HasRows)
        {
            return ParseData(reader, mapping);
        }

        return null;
    }

    public int Upsert(ClientConnection clientConnection)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(Upsert));
        ArgumentNullException.ThrowIfNull(clientConnection);

        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.ClientConnections_Upsert";

        command.Parameters.Add(new SqlParameter("@UserId", ProviderExtensions.ReplaceDbNulls(clientConnection.UserId)));
        command.Parameters.Add(new SqlParameter("@ServerId", ProviderExtensions.ReplaceDbNulls(clientConnection.ServerId)));
        command.Parameters.Add(new SqlParameter("@ClientName", ProviderExtensions.ReplaceDbNulls(clientConnection.ClientName)));
        command.Parameters.Add(new SqlParameter("@ClientUniqueId", ProviderExtensions.ReplaceDbNulls(clientConnection.ClientUniqueId)));

        var id = new SqlParameter("@Id", SqlDbType.Int) { Size = int.MaxValue };
        id.Direction = ParameterDirection.Output;
        command.Parameters.Add(id);
        command.ExecuteNonQuery();

        return clientConnection.ClientId = (int)(ProviderExtensions.ReplaceDbNulls(id.Value) ?? 0);
    }

    public void AddClientChannel(int clientId, int channelId)
    {
        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.ClientConnectionsChannels_Add";

        command.Parameters.Add(new SqlParameter("@ClientId", clientId));
        command.Parameters.Add(new SqlParameter("@ChannelId", channelId));

        command.ExecuteNonQuery();
    }

    public void RemoveClientChannel(int clientId, int channelId)
    {
        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.ClientConnectionsChannels_Remove";

        command.Parameters.Add(new SqlParameter("@ClientId", clientId));
        command.Parameters.Add(new SqlParameter("@ChannelId", channelId));

        command.ExecuteNonQuery();
    }

    public void SetClientChannels(int clientId, int[] channelIds)
    {
        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.ClientConnectionsChannels_SetForClient";

        command.Parameters.Add(new SqlParameter("@ClientId", clientId));
        var param = command.Parameters.AddWithValue("@ChannelIds", channelIds.ToDataTable());
        param.SqlDbType = SqlDbType.Structured;
        param.TypeName = "dbo.IdListTableType";

        command.ExecuteNonQuery();
    }

    private static ClientConnection ParseData(SqlDataReader reader, OrdinalColumnMapping mapping)
    {
        var data = new ClientConnection
        {
            UserId = reader.GetInt32(mapping.UserId),
            ServerId = reader.GetInt32(mapping.ServerId),
            ClientId = reader.GetInt32(mapping.ClientId),
            ClientName = reader.GetString(mapping.ClientName),
            ClientUniqueId = reader.GetGuid(mapping.ClientUniqueId),
        };

        return data;
    }

    private static OrdinalColumnMapping GetColumnMapping(SqlDataReader reader)
    {
        var mapping = new OrdinalColumnMapping();

        mapping.ClientId = reader.GetOrdinal("Id");
        mapping.UserId = reader.GetOrdinal("UserId");
        mapping.ServerId = reader.GetOrdinal("ServerId");
        mapping.ClientName = reader.GetOrdinal("ClientName");
        mapping.ClientUniqueId = reader.GetOrdinal("ClientUniqueId");

        return mapping;
    }

    private class OrdinalColumnMapping
    {
        public int UserId;
        public int ClientId;
        public int ServerId;
        public int ClientName;
        public int ClientUniqueId;
    }
}
