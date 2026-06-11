using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

internal sealed class SharedVariableProvider : BaseSqlProvider, ISharedVariableProvider
{
    private readonly string _connectionString;

    public SharedVariableProvider(IConfiguration configuration) : base(configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection connection string not found.");
    }

    public async Task<SharedVariableRow> GetByWorkflowRefIdNameAsync(Guid workflowRefId, string varName, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.SharedVariable_GetBy_WorkflowRefId_Name", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@WorkflowRefId", workflowRefId);
        command.Parameters.AddWithValue("@VarName", varName);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new KeyNotFoundException($"SharedVariable with WorkflowRefId={workflowRefId} VarName={varName} not found.");
    }

    public async Task<SharedVariableRow> InitializeAsync(Guid workflowRefId, string varName, string varType, string valueJson, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.SharedVariable_Initialize", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@WorkflowRefId", workflowRefId);
        command.Parameters.AddWithValue("@VarName", varName);
        command.Parameters.AddWithValue("@VarType", varType);
        command.Parameters.AddWithValue("@ValueJson", valueJson);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new InvalidOperationException("SharedVariable_Initialize did not return a row.");
    }

    public async Task<SharedVariableRow> SetAsync(Guid workflowRefId, string varName, string valueJson, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.SharedVariable_Set", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@WorkflowRefId", workflowRefId);
        command.Parameters.AddWithValue("@VarName", varName);
        command.Parameters.AddWithValue("@ValueJson", valueJson);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new InvalidOperationException("SharedVariable_Set did not return a row.");
    }

    public async Task<string> IncrementAsync(Guid workflowRefId, string varName, long delta, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.SharedVariable_Increment", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@WorkflowRefId", workflowRefId);
        command.Parameters.AddWithValue("@VarName", varName);
        command.Parameters.AddWithValue("@Delta", delta);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return reader.GetString(0);
        }

        throw new InvalidOperationException("SharedVariable_Increment did not return a value.");
    }

    public async Task<string> DecrementAsync(Guid workflowRefId, string varName, long delta, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.SharedVariable_Decrement", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@WorkflowRefId", workflowRefId);
        command.Parameters.AddWithValue("@VarName", varName);
        command.Parameters.AddWithValue("@Delta", delta);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return reader.GetString(0);
        }

        throw new InvalidOperationException("SharedVariable_Decrement did not return a value.");
    }

    public async Task<int> CompareAndSetAsync(Guid workflowRefId, string varName, string expected, string newValue, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.SharedVariable_CompareAndSet", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@WorkflowRefId", workflowRefId);
        command.Parameters.AddWithValue("@VarName", varName);
        command.Parameters.AddWithValue("@Expected", expected);
        command.Parameters.AddWithValue("@NewValue", newValue);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return reader.GetInt32(0);
        }

        throw new InvalidOperationException("SharedVariable_CompareAndSet did not return a value.");
    }

    internal static SharedVariableRow Map(DbDataReader reader)
    {
        return new SharedVariableRow
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            WorkflowRefId = reader.GetGuid(reader.GetOrdinal("WorkflowRefId")),
            VarName = reader.GetString(reader.GetOrdinal("VarName")),
            VarType = reader.GetString(reader.GetOrdinal("VarType")),
            ValueJson = reader.GetString(reader.GetOrdinal("ValueJson")),
            UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
        };
    }
}
