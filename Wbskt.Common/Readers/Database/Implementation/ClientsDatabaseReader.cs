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

    public void RegisterSqlDependency(OnChangeEventHandler onChange)
    {
        try
        {
            var connectionString = _connectionStringProvider.ConnectionString;
            using var connection = new SqlConnection(connectionString);
            // The query for dependency must be specific and match what the notification engine can support.
            // We select a single, changing column.
            using var command = new SqlCommand("SELECT LastModified FROM dbo.Clients", connection);

            var dependency = new SqlDependency(command);
            dependency.OnChange += onChange;

            connection.Open();
            // A reader must be executed for the dependency to be registered on the server.
            command.ExecuteReader(CommandBehavior.CloseConnection);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register SQL dependency for Clients.");
            // Depending on policy, you might want to re-throw or handle this.
        }
    }
}
