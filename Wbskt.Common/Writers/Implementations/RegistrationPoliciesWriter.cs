using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Common.Readers;
using Wbskt.Common.Records;

namespace Wbskt.Common.Writers.Implementations;

/// <summary>
/// Concrete implementation for writing registration policy data to the database.
/// </summary>
internal sealed class RegistrationPoliciesWriter : IRegistrationPoliciesWriter
{
    private readonly IConnectionStringProvider _connectionStringProvider;

    public RegistrationPoliciesWriter(IConnectionStringProvider connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<int> InsertAsync(RegistrationPolicyRecord policy, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.RegistrationPolicies_Insert";
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RefId", policy.RefId);
        command.Parameters.AddWithValue("@Name", policy.Name);
        command.Parameters.AddWithValue("@UserId", policy.UserId);
        command.Parameters.AddWithValue("@MaxClients", (object?)policy.MaxClients ?? DBNull.Value);
        command.Parameters.AddWithValue("@Expiry", (object?)policy.Expiry ?? DBNull.Value);
        command.Parameters.AddWithValue("@Pin", policy.Pin);

        var idParameter = command.Parameters.Add("@Id", SqlDbType.Int);
        idParameter.Direction = ParameterDirection.Output;

        await command.ExecuteNonQueryAsync(cancellationToken);

        return (int)idParameter.Value;
    }
}
