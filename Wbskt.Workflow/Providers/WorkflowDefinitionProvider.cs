using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

internal sealed class WorkflowDefinitionProvider : BaseSqlProvider, IWorkflowDefinitionProvider
{
    private readonly string _connectionString;

    public WorkflowDefinitionProvider(IConfiguration configuration) : base(configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection connection string not found.");
    }

    public async Task<int?> FindByRefIdVersionAsync(Guid refId, int version, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.WorkflowDefinition_FindBy_RefId_Version", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RefId", refId);
        command.Parameters.AddWithValue("@Version", version);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return reader.GetInt32(0);
        }

        return null;
    }

    public async Task<WorkflowDefinitionRow> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.WorkflowDefinition_GetBy_Id", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@Id", id);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new KeyNotFoundException($"WorkflowDefinition with Id={id} not found.");
    }

    public async Task<WorkflowDefinitionRow> GetByRefIdVersionAsync(Guid refId, int version, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.WorkflowDefinition_GetBy_RefId_Version", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RefId", refId);
        command.Parameters.AddWithValue("@Version", version);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new KeyNotFoundException($"WorkflowDefinition with RefId={refId} Version={version} not found.");
    }

    public async Task<WorkflowDefinitionRow> GetCurrentByRefIdAsync(Guid refId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.WorkflowDefinition_GetLatestVersion_By_RefId", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RefId", refId);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new KeyNotFoundException($"WorkflowDefinition with RefId={refId} not found.");
    }

    public async Task<WorkflowDefinitionRow> InsertAsync(WorkflowDefinitionRow row, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.WorkflowDefinition_Publish", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RefId", row.RefId);
        command.Parameters.AddWithValue("@WorkspaceId", row.WorkspaceId);
        command.Parameters.AddWithValue("@Name", row.Name);
        command.Parameters.AddWithValue("@Description", (object?)row.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("@IsEnabled", row.IsEnabled);
        command.Parameters.AddWithValue("@DefinitionJson", row.DefinitionJson);
        command.Parameters.AddWithValue("@PublishedBy", row.PublishedBy);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new InvalidOperationException("WorkflowDefinition_Publish did not return a row.");
    }

    public async Task DeprecateAsync(int id, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.WorkflowDefinition_UpdateIsEnabled", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@IsEnabled", false);

        await connection.OpenAsync(ct);
        await command.ExecuteNonQueryAsync(ct);
    }

    internal static WorkflowDefinitionRow Map(DbDataReader reader)
    {
        return new WorkflowDefinitionRow
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            RefId = reader.GetGuid(reader.GetOrdinal("RefId")),
            Version = reader.GetInt32(reader.GetOrdinal("Version")),
            WorkspaceId = reader.GetInt32(reader.GetOrdinal("WorkspaceId")),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
            IsEnabled = reader.GetBoolean(reader.GetOrdinal("IsEnabled")),
            DefinitionJson = reader.GetString(reader.GetOrdinal("DefinitionJson")),
            PublishedBy = reader.GetInt32(reader.GetOrdinal("PublishedBy")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
        };
    }

}
