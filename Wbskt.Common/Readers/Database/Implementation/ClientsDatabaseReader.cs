using System.Data;
using System.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database.Implementation;

/// <summary>
/// Concrete implementation for reading client data from the database, with support for SqlDependency.
/// </summary>
internal sealed class ClientsDatabaseReader : IClientsDatabaseReader
{
    private readonly ILogger<ClientsDatabaseReader> _logger;
    private readonly IConnectionStringProvider _connectionStringProvider;

    public ClientsDatabaseReader(ILogger<ClientsDatabaseReader> logger, IConnectionStringProvider connectionStringProvider)
    {
        _logger = logger;
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<List<ClientRecord>> GetAllAsync(DateTime lastModified, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.Clients_GetAll";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@LastModified", lastModified);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var clients = new List<ClientRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            clients.Add(new ClientRecord
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                RefId = reader.GetGuid(reader.GetOrdinal("RefId")),
                UserId = reader.GetInt32(reader.GetOrdinal("UserId")),
                RegistrationPolicyId = reader.GetInt32(reader.GetOrdinal("RegistrationPolicyId")),
                Name = reader.IsDBNull(reader.GetOrdinal("Name")) ? null : reader.GetString(reader.GetOrdinal("Name")),
                Active = reader.GetBoolean(reader.GetOrdinal("Active")),
                LastModified = reader.GetDateTime(reader.GetOrdinal("LastModified"))
            });
        }
        return clients;
    }
}
