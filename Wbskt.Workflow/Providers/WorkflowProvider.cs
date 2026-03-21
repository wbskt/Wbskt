using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Models;
using Wbskt.Workflow.Entities;

namespace Wbskt.Workflow.Providers;

public sealed class WorkflowProvider : BaseSqlProvider, IWorkflowProvider
{
    public WorkflowProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<int> FindIdByRefIdAsync(Guid referenceId, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>(
            "dbo.Workflow_FindBy_RefId",
            p => p.AddWithValue("@RefId", referenceId),
            cancellationToken);
    }

    public async Task<IPagedList<WorkflowEntity>> GetAllByWorkspaceAsync(int workspaceId, CancellationToken cancellationToken = default)
    {
        return await ExecutePagedCollectionAsync(
            "dbo.Workflow_GetAllBy_Workspace",
            p =>
            {
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.Add("@TotalCount", SqlDbType.Int).Direction = ParameterDirection.Output;
            },
            MapWorkflow,
            cancellationToken);
    }

    public async Task<WorkflowEntity> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await ExecuteSingleAsync(
            "dbo.Workflow_GetBy_Id",
            p => p.AddWithValue("@Id", id),
            MapWorkflow,
            null,
            cancellationToken);
    }

    public async Task<WorkflowEntity> InsertAsync(int workspaceId, string name, string? description, CancellationToken cancellationToken = default)
    {
        return await ExecuteSingleAsync(
            "dbo.Workflow_Create",
            p =>
            {
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.AddWithValue("@Name", name);
                p.AddWithValue("@Description", description ?? string.Empty);
            },
            MapWorkflow,
            null,
            cancellationToken);
    }

    public async Task UpdateAsync(int id, string name, string description, bool isEnabled, string definitionJson, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync(
            "dbo.Workflow_Update",
            p =>
            {
                p.AddWithValue("@Id", id);
                p.AddWithValue("@Name", name);
                p.AddWithValue("@Description", description);
                p.AddWithValue("@IsEnabled", isEnabled);
                p.AddWithValue("@DefinitionJson", definitionJson);
            },
            cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync(
            "dbo.Workflow_Delete",
            p => p.AddWithValue("@Id", id),
            cancellationToken);
    }

    public async Task<IReadOnlyCollection<WorkflowEntity>> GetAllEnabledAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync(
            "dbo.Workflow_GetAllEnabled",
            null,
            MapWorkflow,
            cancellationToken);
    }

    public async Task<WorkflowEntity> GetByRefIdAsync(Guid refId, CancellationToken cancellationToken = default)
    {
        return await ExecuteSingleAsync(
            "dbo.Workflow_GetBy_RefId",
            p => p.AddWithValue("@RefId", refId),
            MapWorkflow,
            null,
            cancellationToken);
    }

    private static WorkflowEntity MapWorkflow(SqlDataReader reader)
    {
        return new WorkflowEntity
        {
            Id = reader.GetInt32("Id"),
            RefId = reader.GetGuid("RefId"),
            WorkspaceId = reader.GetInt32("WorkspaceId"),
            Name = reader.GetString("Name"),
            Description = reader.GetString("Description"),
            IsEnabled = reader.GetBoolean("IsEnabled"),
            Version = reader.GetInt32("Version"),
            DefinitionJson = reader.GetString("DefinitionJson"),
            CreatedAt = reader.GetDateTime("CreatedAt")
        };
    }
}
