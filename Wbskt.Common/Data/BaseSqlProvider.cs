using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Common.Abstraction.Models;

namespace Wbskt.Common.Data;

public abstract class BaseSqlProvider
{
    private readonly string _connectionString;

    protected BaseSqlProvider(IConfiguration configuration, string connectionStringName = "DefaultConnection")
    {
        _connectionString = configuration.GetConnectionString(connectionStringName) 
                            ?? throw new ArgumentNullException(nameof(configuration));
    }

    protected async Task<T> ExecuteSingleAsync<T>(
        string procedureName, 
        Action<SqlParameterCollection> addParameters, 
        Func<SqlDataReader, T> map,
        Exception? exceptionIfNotFound = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(procedureName, connection);
        command.CommandType = CommandType.StoredProcedure;

        addParameters(command.Parameters);

        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            return map(reader);
        }

        throw exceptionIfNotFound ?? new KeyNotFoundException($"Record not found in {procedureName}.");
    }

    protected async Task<IReadOnlyCollection<T>> ExecuteCollectionAsync<T>(
        string procedureName, 
        Action<SqlParameterCollection>? addParameters, 
        Func<SqlDataReader, T> map,
        CancellationToken cancellationToken = default)
    {
        var result = new List<T>();

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(procedureName, connection);
        command.CommandType = CommandType.StoredProcedure;

        addParameters?.Invoke(command.Parameters);

        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(map(reader));
        }

        return result.AsReadOnly();
    }

    protected async Task<IPagedList<T>> ExecutePagedCollectionAsync<T>(
        string procedureName, 
        Action<SqlParameterCollection>? addParameters, 
        Func<SqlDataReader, T> map,
        CancellationToken cancellationToken = default)
    {
        var items = new List<T>();
        var totalCount = 0;

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(procedureName, connection);
        command.CommandType = CommandType.StoredProcedure;

        addParameters?.Invoke(command.Parameters);

        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(map(reader));
        }
        
        await reader.CloseAsync();

        if (command.Parameters.Contains("@TotalCount"))
        {
            totalCount = (int)command.Parameters["@TotalCount"].Value;
        }

        return new PagedList<T>(items, totalCount);
    }

    protected async Task<SqlParameterCollection> ExecuteNonQueryAsync(
        string procedureName, 
        Action<SqlParameterCollection>? addParameters,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(procedureName, connection);
        command.CommandType = CommandType.StoredProcedure;

        addParameters?.Invoke(command.Parameters);

        await connection.OpenAsync(cancellationToken);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return command.Parameters;
    }

    protected async Task<T?> ExecuteScalarAsync<T>(
        string procedureName, 
        Action<SqlParameterCollection>? addParameters,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(procedureName, connection);
        command.CommandType = CommandType.StoredProcedure;

        addParameters?.Invoke(command.Parameters);

        await connection.OpenAsync(cancellationToken);
        var result = await command.ExecuteScalarAsync(cancellationToken);

        if (result == null || result == DBNull.Value)
        {
            return default;
        }

        return (T)result;
    }
}
