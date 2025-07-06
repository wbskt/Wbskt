using System.Data;
using System.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database.Implementation;

internal sealed class ClientsChannelsDatabaseReader(ILogger<ClientsChannelsDatabaseReader> logger, IConnectionStringProvider connectionStringProvider) : IClientsChannelsDatabaseReader
{
    public IReadOnlyCollection<ClientChannelReadRecord> GetAll(DateTime lastModified)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(GetAll));
        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.ClientsChannels_GetAll";

        command.Parameters.Add(new SqlParameter("@LastModified", lastModified));

        var result = new List<ClientChannelReadRecord>();
        using var reader = command.ExecuteReader();
        var mapping = GetColumnMapping(reader);

        while (reader.Read())
        {
            result.Add(ParseData(reader, mapping));
        }

        return result.AsReadOnly();
    }

    internal void RegisterSqlDependency(OnChangeEventHandler onDatabaseChange)
    {
        try
        {
            using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
            using var command = new SqlCommand("SELECT LastModified FROM dbo.ClientsChannels", connection); // listen to changes in this output

            var dependency = new SqlDependency(command);
            dependency.OnChange += onDatabaseChange;

            connection.Open();
            using var reader = command.ExecuteReader(); // must execute to register dependency
        }
        catch (SqlException sex)
        {
            logger.LogError(sex, "failed to register SQL dependency. {message}", sex.Message);
        }
    }

    private static ClientChannelReadRecord ParseData(SqlDataReader reader, OrdinalColumnMapping mapping)
    {
        return new ClientChannelReadRecord
        {
            ClientId = reader.GetInt32(mapping.ClientId),
            ChannelId = reader.GetInt32(mapping.ChannelId),
            LastModified = reader.GetDateTime(mapping.LastModified),
            Deleted = reader.GetBoolean(mapping.Deleted)
        };
    }

    private static OrdinalColumnMapping GetColumnMapping(SqlDataReader reader)
    {
        return new OrdinalColumnMapping
        {
            ClientId = reader.GetOrdinal("ClientId"),
            ChannelId = reader.GetOrdinal("ChannelId"),
            LastModified = reader.GetOrdinal("LastModified"),
            Deleted = reader.GetOrdinal("Deleted")
        };
    }

    private class OrdinalColumnMapping
    {
        public int ClientId;
        public int ChannelId;
        public int LastModified;
        public int Deleted;
    }
}
