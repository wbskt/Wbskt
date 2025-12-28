using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Common.Readers.Database;
using Wbskt.Common.Records;

namespace Wbskt.Common.Writers.Implementations;

internal sealed class UsersWriter : IUsersWriter
{
    private readonly IConnectionStringProvider _connectionStringProvider;

    public UsersWriter(IConnectionStringProvider connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
    }

    public async Task<int> InsertAsync(UserRecord user, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.Users_Insert";
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@Name", user.Name);
        command.Parameters.AddWithValue("@EmailId", user.Email);
        command.Parameters.AddWithValue("@PasswordHash", user.PasswordHash);

        var idParameter = command.Parameters.Add("@Id", SqlDbType.Int);
        idParameter.Direction = ParameterDirection.Output;

        await command.ExecuteNonQueryAsync(cancellationToken);

        return (int)idParameter.Value;
    }
}
