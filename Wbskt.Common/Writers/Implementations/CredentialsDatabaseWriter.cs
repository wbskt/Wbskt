using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Common.Readers.Database;
using Wbskt.Common.Records;

namespace Wbskt.Common.Writers.Implementations;

internal sealed class CredentialsDatabaseWriter : ICredentialsWriter
{
    private readonly IConnectionStringProvider _connectionStringProvider;

    public CredentialsDatabaseWriter(IConnectionStringProvider connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task UpsertAsync(CredentialRecord credential, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.Credentials_Upsert";
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@UserId", credential.UserId);
        command.Parameters.AddWithValue("@IntegrationType", credential.IntegrationType);
        command.Parameters.AddWithValue("@Name", credential.Name);
        command.Parameters.AddWithValue("@EncryptedCredentials", credential.EncryptedCredentials);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, int userId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.Credentials_Delete_ById";
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@UserId", userId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
