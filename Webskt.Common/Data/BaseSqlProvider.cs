using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Webskt.Common.Data;

public abstract class BaseSqlProvider
{
    private readonly string _connectionString;

    protected BaseSqlProvider(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") 
                            ?? throw new ArgumentNullException(nameof(configuration));
    }

    protected async Task<T> ExecuteSingleAsync<T>(
        string procedureName, 
        Action<SqlParameterCollection> addParameters, 
        Func<SqlDataReader, T> map,
        Exception? exceptionIfNotFound = null)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(procedureName, connection);
        command.CommandType = CommandType.StoredProcedure;

        addParameters(command.Parameters);

        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return map(reader);
        }

        throw exceptionIfNotFound ?? new KeyNotFoundException($"Record not found in {procedureName}.");
    }

    protected async Task<IReadOnlyCollection<T>> ExecuteCollectionAsync<T>(
        string procedureName, 
        Action<SqlParameterCollection>? addParameters, 
        Func<SqlDataReader, T> map)
    {
        var result = new List<T>();

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(procedureName, connection);
        command.CommandType = CommandType.StoredProcedure;

        addParameters?.Invoke(command.Parameters);

        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            result.Add(map(reader));
        }

        return result.AsReadOnly();
    }

    protected async Task<SqlParameterCollection> ExecuteNonQueryAsync(
        string procedureName, 
        Action<SqlParameterCollection>? addParameters)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(procedureName, connection);
        command.CommandType = CommandType.StoredProcedure;

        addParameters?.Invoke(command.Parameters);

        await connection.OpenAsync();
        await command.ExecuteNonQueryAsync();

        return command.Parameters;
    }

    protected async Task<T?> ExecuteScalarAsync<T>(
        string procedureName, 
        Action<SqlParameterCollection>? addParameters)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(procedureName, connection);
        command.CommandType = CommandType.StoredProcedure;

        addParameters?.Invoke(command.Parameters);

        await connection.OpenAsync();
        var result = await command.ExecuteScalarAsync();

        if (result == null || result == DBNull.Value)
        {
            return default;
        }

        return (T)result;
    }
}
