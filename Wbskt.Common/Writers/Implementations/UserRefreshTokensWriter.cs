using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Common.Readers.Database;
using Wbskt.Common.Records;

namespace Wbskt.Common.Writers.Implementations;

internal sealed class UserRefreshTokensWriter : IUserRefreshTokensWriter
{
    private readonly IConnectionStringProvider _connectionStringProvider;

    public UserRefreshTokensWriter(IConnectionStringProvider connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task InsertAsync(RefreshTokenRecord refreshToken, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.UserRefreshTokens_Insert";
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@UserId", refreshToken.UserId);
        command.Parameters.AddWithValue("@Token", refreshToken.Token);
        command.Parameters.AddWithValue("@Expires", refreshToken.Expires);
        command.Parameters.AddWithValue("@CreatedByIp", refreshToken.CreatedByIp);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateAsync(RefreshTokenRecord refreshToken, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.UserRefreshTokens_Update";
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@Id", refreshToken.Id);
        command.Parameters.AddWithValue("@Revoked", refreshToken.Revoked);
        command.Parameters.AddWithValue("@RevokedByIp", refreshToken.RevokedByIp);
        command.Parameters.AddWithValue("@ReplacedByToken", refreshToken.ReplacedByToken);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
