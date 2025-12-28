using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database.Implementation;

internal sealed class ServersDatabaseReader : IServersDatabaseReader
{
    private readonly IConnectionStringProvider _connectionStringProvider;

    public ServersDatabaseReader(IConnectionStringProvider connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
    }

    public async Task<List<ServerRecord>> GetAllAsync(DateTime lastModified, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.Servers_GetAll";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@LastModified", lastModified);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var servers = new List<ServerRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            servers.Add(new ServerRecord
            {
                Id = reader.GetInt32(0),
                PublicDomainName = reader.GetString(1),
                Status = reader.GetInt32(2),
                LastModified = reader.GetDateTime(3)
            });
        }

        return servers;
    }
}
