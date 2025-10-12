using System.Data;
using System.Data.SqlClient;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database.Implementation;

/// <summary>
/// Concrete implementation for reading registration policy data from the database.
/// </summary>
internal sealed class RegistrationPoliciesDatabaseReader : IRegistrationPoliciesDatabaseReader
{
    private readonly IConnectionStringProvider _connectionStringProvider;

    public RegistrationPoliciesDatabaseReader(IConnectionStringProvider connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<List<RegistrationPolicyRecord>> GetAllAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.RegistrationPolicies_GetAll_ByUserId";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@UserId", userId);

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
                Pin = reader.GetInt32(reader.GetOrdinal("Pin"))
            });
        }

        return policies;
    }
}
