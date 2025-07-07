using System.Data;
using System.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database.Implementation;

internal sealed class PublishersChannelsDatabaseReader(ILogger<PublishersChannelsDatabaseReader> logger, IConnectionStringProvider connectionStringProvider) : IPublishersChannelsDatabaseReader
{
    public IReadOnlyCollection<PublisherChannelReadRecord> GetAll(DateTime lastModified)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(GetAll));
        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.PublishersChannels_GetAll";

        command.Parameters.Add(new SqlParameter("@LastModified", lastModified));

        var result = new List<PublisherChannelReadRecord>();
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
            using var command = new SqlCommand("SELECT LastModified FROM dbo.PublishersChannels", connection);

            var dependency = new SqlDependency(command);
            dependency.OnChange += onDatabaseChange;

            connection.Open();
            using var reader = command.ExecuteReader();
        }
        catch (SqlException sex)
        {
            logger.LogError(sex, "failed to register SQL dependency. {message}", sex.Message);
        }
    }

    private static PublisherChannelReadRecord ParseData(SqlDataReader reader, OrdinalColumnMapping mapping)
    {
        var data = new PublisherChannelReadRecord
        {
            Deleted = reader.GetBoolean(mapping.Deleted),
            ChannelId = reader.GetInt32(mapping.ChannelId),
            PublisherId = reader.GetInt32(mapping.PublisherId),
            LastModified = reader.GetDateTime(mapping.LastModified)
        };

        return data;
    }

    private static OrdinalColumnMapping GetColumnMapping(SqlDataReader reader)
    {
        var mapping = new OrdinalColumnMapping
        {
            Deleted = reader.GetOrdinal("Deleted"),
            ChannelId = reader.GetOrdinal("ChannelId"),
            PublisherId = reader.GetOrdinal("PublisherId"),
            LastModified = reader.GetOrdinal("LastModified")
        };

        return mapping;
    }

    private class OrdinalColumnMapping
    {
        public int ChannelId;
        public int Deleted;
        public int LastModified;
        public int PublisherId;
    }
}
