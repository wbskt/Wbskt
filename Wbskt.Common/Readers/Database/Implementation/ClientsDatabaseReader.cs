using System.Data;
using System.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database.Implementation;

internal sealed class ClientsDatabaseReader(ILogger<ClientsDatabaseReader> logger, IConnectionStringProvider connectionStringProvider) : IClientsDatabaseReader
{
    public IReadOnlyCollection<ClientReadRecord> GetAll(DateTime lastModified)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(GetAll));
        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.Clients_GetAll";

        command.Parameters.Add(new SqlParameter("@LastModified", lastModified));

        var result = new List<ClientReadRecord>();
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
            using var command = new SqlCommand("SELECT LastModified FROM dbo.Clients", connection); // listen to changes in this output

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

    private static ClientReadRecord ParseData(SqlDataReader reader, OrdinalColumnMapping mapping)
    {
        return new ClientReadRecord
        {
            Id = reader.GetInt32(mapping.Id),
            Name = reader.GetString(mapping.Name),
            UserId = reader.GetInt32(mapping.UserId),
            ServerId = reader.GetInt32(mapping.ServerId),
            UniqueRef = reader.GetGuid(mapping.UniqueRef),
            LastModified = reader.GetDateTime(mapping.LastModified)
        };
    }

    private static OrdinalColumnMapping GetColumnMapping(SqlDataReader reader)
    {
        return new OrdinalColumnMapping
        {
            Id = reader.GetOrdinal("Id"),
            Name = reader.GetOrdinal("Name"),
            UserId = reader.GetOrdinal("UserId"),
            ServerId = reader.GetOrdinal("ServerId"),
            UniqueRef = reader.GetOrdinal("UniqueRef"),
            LastModified = reader.GetOrdinal("LastModified")
        };
    }

    private class OrdinalColumnMapping
    {
        public int Id;
        public int LastModified;
        public int Name;
        public int ServerId;
        public int UniqueRef;
        public int UserId;
    }
}
