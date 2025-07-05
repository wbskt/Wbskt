using System.Data;
using System.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Providers;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Implementations;

internal sealed class ChannelsDatabaseReader(ILogger<ChannelsDatabaseReader> logger, IConnectionStringProvider connectionStringProvider) : IChannelsDatabaseReader
{
    public IReadOnlyCollection<ChannelReadRecord> GetAll(DateTime lastModified)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(GetAll));
        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.Channels_GetAll";

        command.Parameters.Add(new SqlParameter("@LastModified", lastModified));

        var result = new List<ChannelReadRecord>();
        using var reader = command.ExecuteReader();
        var mapping = GetColumnMapping(reader);

        while (reader.Read())
        {
            result.Add(ParseData(reader, mapping));
        }

        return result.AsReadOnly();
    }

    public IReadOnlyCollection<ChannelReadRecord> GetAllByUserId(DateTime lastModified, int userId)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(GetAllByUserId));
        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.Channels_GetAll_UserId";

        command.Parameters.Add(new SqlParameter("@UserId", userId));
        command.Parameters.Add(new SqlParameter("@LastModified", lastModified));

        var result = new List<ChannelReadRecord>();
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
            using var command = new SqlCommand("SELECT LastModified FROM dbo.Channels", connection); // listen to changes in this output

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

    private static ChannelReadRecord ParseData(SqlDataReader reader, OrdinalColumnMapping mapping)
    {
        var data = new ChannelReadRecord
        {
            Id = reader.GetInt32(mapping.Id),
            Name = reader.GetString(mapping.Name),
            UserId = reader.GetInt32(mapping.UserId),
            LastModified = reader.GetDateTime(mapping.LastModified),
            SubscriptionRef = reader.GetGuid(mapping.SubscriptionRef)
        };

        return data;
    }

    private static OrdinalColumnMapping GetColumnMapping(SqlDataReader reader)
    {
        var mapping = new OrdinalColumnMapping
        {
            Id = reader.GetOrdinal("Id"),
            Name = reader.GetOrdinal("Name"),
            UserId = reader.GetOrdinal("UserId"),
            LastModified = reader.GetOrdinal("LastModified"),
            SubscriptionRef = reader.GetOrdinal("SubscriptionRef")
        };

        return mapping;
    }

    private class OrdinalColumnMapping
    {
        public int Id;
        public int Name;
        public int UserId;
        public int LastModified;
        public int SubscriptionRef;
    }
}
