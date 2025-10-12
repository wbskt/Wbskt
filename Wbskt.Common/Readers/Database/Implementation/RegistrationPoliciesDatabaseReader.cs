using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database.Implementation;

/// <summary>
/// Concrete implementation for reading registration policy data from the database, with support for SqlDependency.
/// </summary>
internal sealed class RegistrationPoliciesDatabaseReader : IRegistrationPoliciesDatabaseReader
{
    private readonly ILogger<RegistrationPoliciesDatabaseReader> _logger;
    private readonly IConnectionStringProvider _connectionStringProvider;

    public RegistrationPoliciesDatabaseReader(ILogger<RegistrationPoliciesDatabaseReader> logger, IConnectionStringProvider connectionStringProvider)
    {
        _logger = logger;
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<List<RegistrationPolicyRecord>> GetAllAsync(DateTime lastModified, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.RegistrationPolicies_GetAll";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@LastModified", lastModified);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var policies = new List<RegistrationPolicyRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            policies.Add(new RegistrationPolicyRecord
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                RefId = reader.GetGuid(reader.GetOrdinal("RefId")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                UserId = reader.GetInt32(reader.GetOrdinal("UserId")),
                MaxClients = reader.IsDBNull(reader.GetOrdinal("MaxClients")) ? null : reader.GetInt32(reader.GetOrdinal("MaxClients")),
                Expiry = reader.IsDBNull(reader.GetOrdinal("Expiry")) ? null : reader.GetDateTime(reader.GetOrdinal("Expiry")),
                Pin = reader.GetString(reader.GetOrdinal("Pin")),
                LastModified = reader.GetDateTime(reader.GetOrdinal("LastModified"))
            });
        }
        return policies;
    }

    public async Task<RegistrationPolicyRecord?> GetByPinAsync(string pin, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.RegistrationPolicies_GetBy_Pin";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@Pin", pin);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            return new RegistrationPolicyRecord
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                RefId = reader.GetGuid(reader.GetOrdinal("RefId")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                UserId = reader.GetInt32(reader.GetOrdinal("UserId")),
                MaxClients = reader.IsDBNull(reader.GetOrdinal("MaxClients")) ? null : reader.GetInt32(reader.GetOrdinal("MaxClients")),
                Expiry = reader.IsDBNull(reader.GetOrdinal("Expiry")) ? null : reader.GetDateTime(reader.GetOrdinal("Expiry")),
                Pin = reader.GetString(reader.GetOrdinal("Pin")),
                LastModified = reader.GetDateTime(reader.GetOrdinal("LastModified"))
            };
        }

        return null;
    }
}
