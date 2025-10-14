using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database.Implementation;

internal sealed class CredentialsDatabaseReader : ICredentialsReader
{
    private readonly IConnectionStringProvider _connectionStringProvider;

    public CredentialsDatabaseReader(IConnectionStringProvider connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<List<CredentialRecord>> GetAllForUserAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.Credentials_GetAll_ByUserId";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@UserId", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var credentials = new List<CredentialRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            credentials.Add(new CredentialRecord
            {
                Id = reader.GetInt32(0),
                UserId = reader.GetInt32(1),
                IntegrationType = reader.GetString(2),
                Name = reader.GetString(3),
                EncryptedCredentials = "", // Not returned for security
                LastModified = reader.GetDateTime(4)
            });
        }

        return credentials;
    }

    public async Task<string?> GetEncryptedCredentialsAsync(int userId, string name, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.Credentials_Get_ByUserIdAndName";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Name", name);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }
}
