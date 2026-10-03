using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Models;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

internal sealed class WorkflowDefinitionProvider : BaseSqlProvider, IWorkflowDefinitionProvider
{
    public WorkflowDefinitionProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<int?> FindByRefIdVersionAsync(Guid refId, int version, CancellationToken ct)
    {
        var result = await ExecuteScalarAsync<object>(
            "dbo.WorkflowDefinition_FindBy_RefId_Version",
            p =>
            {
                p.AddWithValue("@RefId", refId);
                p.AddWithValue("@Version", version);
            },
            ct
        );
        return result is int id ? id : (result == null ? null : Convert.ToInt32(result));
    }

    public async Task<WorkflowDefinitionRow> GetByIdAsync(int id, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.WorkflowDefinition_GetBy_Id",
            p => p.AddWithValue("@Id", id),
            Map,
            new KeyNotFoundException($"WorkflowDefinition with Id={id} not found."),
            ct
        );
    }

    public async Task<WorkflowDefinitionRow> GetByRefIdVersionAsync(Guid refId, int version, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.WorkflowDefinition_GetBy_RefId_Version",
            p =>
            {
                p.AddWithValue("@RefId", refId);
                p.AddWithValue("@Version", version);
            },
            Map,
            new KeyNotFoundException($"WorkflowDefinition with RefId={refId} Version={version} not found."),
            ct
        );
    }

    public async Task<WorkflowDefinitionRow> GetCurrentByRefIdAsync(Guid refId, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.WorkflowDefinition_GetLatestVersion_By_RefId",
            p => p.AddWithValue("@RefId", refId),
            Map,
            new KeyNotFoundException($"WorkflowDefinition with RefId={refId} not found."),
            ct
        );
    }

    public async Task<WorkflowDefinitionRow> InsertAsync(WorkflowDefinitionRow row, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.WorkflowDefinition_Publish",
            p =>
            {
                p.AddWithValue("@RefId", row.RefId);
                p.AddWithValue("@WorkspaceId", row.WorkspaceId);
                p.AddWithValue("@Name", row.Name);
                p.AddWithValue("@Description", (object?)row.Description ?? DBNull.Value);
                p.AddWithValue("@IsEnabled", row.IsEnabled);
                p.AddWithValue("@DefinitionJson", row.DefinitionJson);
                p.AddWithValue("@PublishedBy", row.PublishedBy);
            },
            Map,
            new InvalidOperationException("WorkflowDefinition_Publish did not return a row."),
            ct
        );
    }

    public async Task<IPagedList<WorkflowDefinitionRow>> GetAllSummariesAsync(int workspaceId, int skip, int take, CancellationToken ct)
    {
        return await ExecutePagedCollectionAsync(
            "dbo.WorkflowDefinition_GetAll_Summaries_By_WorkspaceId",
            p =>
            {
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.AddWithValue("@Skip", skip);
                p.AddWithValue("@Take", take);
                p.Add("@TotalCount", SqlDbType.Int).Direction = ParameterDirection.Output;
            },
            Map,
            ct
        );
    }

    public async Task DeprecateAsync(int id, CancellationToken ct)
    {
        await SetEnabledAsync(id, false, ct);
    }

    public async Task SetEnabledAsync(int id, bool isEnabled, CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            "dbo.WorkflowDefinition_UpdateIsEnabled",
            p =>
            {
                p.AddWithValue("@Id", id);
                p.AddWithValue("@IsEnabled", isEnabled);
            },
            ct
        );
    }

    public async Task<bool> DeleteUnreferencedAsync(int id, CancellationToken ct)
    {
        var deleted = await ExecuteScalarAsync<object>(
            "dbo.WorkflowDefinition_DeleteUnreferencedById",
            p => p.AddWithValue("@Id", id),
            ct
        );

        return deleted is not null && Convert.ToInt32(deleted) > 0;
    }

    public async Task<IReadOnlyCollection<WorkflowVersionRow>> GetVersionsAsync(Guid refId, int workspaceId, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.WorkflowDefinition_GetVersions_By_RefId",
            p =>
            {
                p.AddWithValue("@RefId", refId);
                p.AddWithValue("@WorkspaceId", workspaceId);
            },
            reader => new WorkflowVersionRow
            {
                Version = reader.GetInt32(reader.GetOrdinal("Version")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
                IsEnabled = reader.GetBoolean(reader.GetOrdinal("IsEnabled")),
                PublishedBy = reader.GetInt32(reader.GetOrdinal("PublishedBy")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                RunCount = reader.GetInt64(reader.GetOrdinal("RunCount"))
            },
            ct
        );
    }

    public async Task<WorkflowDeletion?> DeleteAsync(Guid refId, int workspaceId, int deletedBy, CancellationToken ct)
    {
        var rows = await ExecuteCollectionAsync(
            "dbo.WorkflowDefinition_Delete",
            p =>
            {
                p.AddWithValue("@RefId", refId);
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.AddWithValue("@DeletedBy", deletedBy);
            },
            reader => (Kind: reader.GetString(reader.GetOrdinal("Kind")), Id: reader.GetInt64(reader.GetOrdinal("Id"))),
            ct
        );

        // Every deleted workflow has at least one definition, so no rows means nothing was deleted.
        if (rows.Count == 0)
        {
            return null;
        }

        return new WorkflowDeletion(
            rows.Where(r => r.Kind == "run").Select(r => r.Id).ToList(),
            rows.Where(r => r.Kind == "definition").Select(r => (int)r.Id).ToList());
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
