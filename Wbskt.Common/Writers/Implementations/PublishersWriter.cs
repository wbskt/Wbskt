using System.Data;
using System.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Extensions;
using Wbskt.Common.Readers;
using Wbskt.Common.Records;

namespace Wbskt.Common.Writers.Implementations;

internal sealed class PublishersWriter(ILogger<PublishersWriter> logger, IConnectionStringProvider connectionStringProvider) : IPublishersWriter
{
    public int InsertPublisher(PublisherRecord record)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(InsertPublisher));
        ArgumentNullException.ThrowIfNull(record);

        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.Publishers_Insert";

        command.Parameters.Add(new SqlParameter("@Name", ProviderExtensions.ReplaceDbNulls(record.Name)));
        command.Parameters.Add(new SqlParameter("@UserId", ProviderExtensions.ReplaceDbNulls(record.UserId)));
        command.Parameters.Add(new SqlParameter("@PublisherRef", ProviderExtensions.ReplaceDbNulls(record.PublisherRef)));

        var id = new SqlParameter("@Id", SqlDbType.Int) { Size = int.MaxValue };
        id.Direction = ParameterDirection.Output;
        command.Parameters.Add(id);
        command.ExecuteNonQuery();

        return (int)(ProviderExtensions.ReplaceDbNulls(id.Value) ?? 0);
    }
}
