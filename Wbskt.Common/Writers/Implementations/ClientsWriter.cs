using System.Data;
using System.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Extensions;
using Wbskt.Common.Readers;
using Wbskt.Common.Records;

namespace Wbskt.Common.Writers.Implementations;

internal sealed class ClientsWriter(ILogger<ClientsWriter> logger, IConnectionStringProvider connectionStringProvider) : IClientsWriter
{
    public int UpsertClient(ClientRecord record)
    {
        logger.LogTrace("DB operation: {functionName}", nameof(UpsertClient));
        ArgumentNullException.ThrowIfNull(record);

        using var connection = new SqlConnection(connectionStringProvider.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "dbo.Clients_Upsert";

        command.Parameters.Add(new SqlParameter("@Name", ProviderExtensions.ReplaceDbNulls(record.Name)));
        command.Parameters.Add(new SqlParameter("@UniqueRef", ProviderExtensions.ReplaceDbNulls(record.UniqueRef)));
        command.Parameters.Add(new SqlParameter("@UserId", ProviderExtensions.ReplaceDbNulls(record.UserId)));
        command.Parameters.Add(new SqlParameter("@ServerId", ProviderExtensions.ReplaceDbNulls(record.ServerId)));

        var id = new SqlParameter("@Id", SqlDbType.Int) { Size = int.MaxValue };
        id.Direction = ParameterDirection.Output;
        command.Parameters.Add(id);
        command.ExecuteNonQuery();

        return (int)(ProviderExtensions.ReplaceDbNulls(id.Value) ?? 0);
    }
}
