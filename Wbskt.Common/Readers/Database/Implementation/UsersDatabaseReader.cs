using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database.Implementation;

internal sealed class UsersDatabaseReader : IUsersDatabaseReader
{
    private readonly IConnectionStringProvider _connectionStringProvider;

    public UsersDatabaseReader(IConnectionStringProvider connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<UserRecord?> GetByIdAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.Users_GetBy_Id";
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@Id", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            return new UserRecord
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Email = reader.GetString(2),
                PasswordHash = reader.GetString(3)
            };
        }

        return null;
    }

    public async Task<UserRecord?> GetByEmailIdAsync(string emailId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.Users_GetBy_EmailId";
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@EmailId", emailId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            return new UserRecord
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Email = reader.GetString(2),
                PasswordHash = reader.GetString(3)
            };
        }

        return null;
    }

    public async Task<int> FindByEmailIdAsync(string emailId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.Users_FindBy_EmailId";
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@EmailId", emailId);

        var result = await command.ExecuteScalarAsync(cancellationToken);

        return result is int userId ? userId : 0;
    }
}
