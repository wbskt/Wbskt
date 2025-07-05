using System.Data;
using System.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Extensions;
using Wbskt.Common.Records;

namespace Wbskt.Common.Providers.Writers.Implementations;

internal class ChannelsWriter(ILogger<ChannelsWriter> logger, IConnectionStringProvider connectionStringProvider) : IChannelsWriter
{
    public int InsertChannel(ChannelRecord record)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(InsertChannel));
        ArgumentNullException.ThrowIfNull(record);

        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.Channels_Insert_Seq";

        command.Parameters.Add(new SqlParameter("@Name", ProviderExtensions.ReplaceDbNulls(record.Name)));
        command.Parameters.Add(new SqlParameter("@UserId", ProviderExtensions.ReplaceDbNulls(record.UserId)));
        command.Parameters.Add(new SqlParameter("@SubscriptionRef", ProviderExtensions.ReplaceDbNulls(record.SubscriptionRef)));

        var id = new SqlParameter("@Id", SqlDbType.Int) { Size = int.MaxValue };
        id.Direction = ParameterDirection.Output;
        command.Parameters.Add(id);
        command.ExecuteNonQuery();

        return (int)(ProviderExtensions.ReplaceDbNulls(id.Value) ?? 0);
    }
}
