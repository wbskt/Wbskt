using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Common.Readers;
using Wbskt.Common.Readers.Database;
using Wbskt.Common.Records;

namespace Wbskt.Common.Writers.Implementations;

/// <summary>
/// Concrete implementation for writing client data to the database.
/// </summary>
internal sealed class ClientsWriter : IClientsWriter
{
    private readonly IConnectionStringProvider _connectionStringProvider;

    public ClientsWriter(IConnectionStringProvider connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
    }

    public async Task<int> UpsertAsync(ClientRecord client, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.Clients_Upsert";
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RefId", client.RefId);
        command.Parameters.AddWithValue("@UserId", client.UserId);
        command.Parameters.AddWithValue("@RegistrationPolicyId", client.RegistrationPolicyId);
        command.Parameters.AddWithValue("@Name", (object?)client.Name ?? DBNull.Value);
        command.Parameters.AddWithValue("@Active", client.Active);

        var idParameter = command.Parameters.Add("@Id", SqlDbType.Int);
        idParameter.Direction = ParameterDirection.Output;

        await command.ExecuteNonQueryAsync(cancellationToken);

        return (int)idParameter.Value;
    }
}
