using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database.Implementation;

internal sealed class UserRefreshTokensDatabaseReader : IUserRefreshTokensDatabaseReader
{
    private readonly IConnectionStringProvider _connectionStringProvider;

    public UserRefreshTokensDatabaseReader(IConnectionStringProvider connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
    }

    public async Task<RefreshTokenRecord?> GetByTokenAsync(string token, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.UserRefreshTokens_GetBy_Token";
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@Token", token);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            return new RefreshTokenRecord
            {
                Id = reader.GetInt32(0),
                UserId = reader.GetInt32(1),
                Token = reader.GetString(2),
                Expires = reader.GetDateTime(3),
                Created = reader.GetDateTime(4),
                CreatedByIp = reader.GetString(5),
                Revoked = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                RevokedByIp = reader.IsDBNull(7) ? null : reader.GetString(7),
                ReplacedByToken = reader.IsDBNull(8) ? null : reader.GetString(8)
            };
        }

        return null;
    }
}
