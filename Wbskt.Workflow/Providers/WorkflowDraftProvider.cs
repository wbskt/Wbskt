using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

internal sealed class WorkflowDraftProvider : BaseSqlProvider, IWorkflowDraftProvider
{
    public WorkflowDraftProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<WorkflowDraftRow?> FindAsync(Guid refId, int workspaceId, CancellationToken ct)
    {
        return await ExecuteFindAsync(
            "dbo.WorkflowDraft_GetBy_RefId",
            p =>
            {
                p.AddWithValue("@RefId", refId);
                p.AddWithValue("@WorkspaceId", workspaceId);
            },
            Map,
            ct
        );
    }

    public async Task<IReadOnlyCollection<WorkflowDraftRow>> ListAsync(int workspaceId, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.WorkflowDraft_ListBy_WorkspaceId",
            p => p.AddWithValue("@WorkspaceId", workspaceId),
            Map,
            ct
        );
    }

    public async Task<WorkflowDraftRow> SaveAsync(WorkflowDraftSave save, int? expectedRevision, int? maxDrafts, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.WorkflowDraft_Save",
            p =>
            {
                p.AddWithValue("@RefId", save.RefId);
                p.AddWithValue("@WorkspaceId", save.WorkspaceId);
                p.AddWithValue("@Name", save.Name);
                p.AddWithValue("@Description", (object?)save.Description ?? DBNull.Value);
                p.AddWithValue("@DefinitionJson", save.DefinitionJson);
                p.AddWithValue("@UserId", save.UserId);
                p.AddWithValue("@UserRefId", (object?)save.UserRefId ?? DBNull.Value);
                p.AddWithValue("@ExpectedRevision", (object?)expectedRevision ?? DBNull.Value);
                p.AddWithValue("@MaxDrafts", (object?)maxDrafts ?? DBNull.Value);
            },
            Map,
            new InvalidOperationException("WorkflowDraft_Save did not return a row."),
            ct
        );
    }

    public async Task<bool> DeleteAsync(Guid refId, int workspaceId, int? expectedRevision, CancellationToken ct)
    {
        var deleted = await ExecuteScalarAsync<object>(
            "dbo.WorkflowDraft_Delete",
            p =>
            {
                p.AddWithValue("@RefId", refId);
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.AddWithValue("@ExpectedRevision", (object?)expectedRevision ?? DBNull.Value);
            },
            ct
        );

        return deleted is not null && Convert.ToInt32(deleted) > 0;
    }

    private static WorkflowDraftRow Map(DbDataReader reader)
    {
        int baseVersion = reader.GetOrdinal("BaseVersion");
        int description = reader.GetOrdinal("Description");
        int updatedByRefId = reader.GetOrdinal("UpdatedByRefId");
        int currentVersion = reader.GetOrdinal("CurrentVersion");

        return new WorkflowDraftRow
        {
            RefId = reader.GetGuid(reader.GetOrdinal("RefId")),
            WorkspaceId = reader.GetInt32(reader.GetOrdinal("WorkspaceId")),
            BaseVersion = reader.IsDBNull(baseVersion) ? null : reader.GetInt32(baseVersion),
            Revision = reader.GetInt32(reader.GetOrdinal("Revision")),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            Description = reader.IsDBNull(description) ? null : reader.GetString(description),
            DefinitionJson = reader.GetString(reader.GetOrdinal("DefinitionJson")),
            CreatedBy = reader.GetInt32(reader.GetOrdinal("CreatedBy")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            UpdatedBy = reader.GetInt32(reader.GetOrdinal("UpdatedBy")),
            UpdatedByRefId = reader.IsDBNull(updatedByRefId) ? null : reader.GetGuid(updatedByRefId),
            UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt")),
            CurrentVersion = reader.IsDBNull(currentVersion) ? null : reader.GetInt32(currentVersion)
        };
    }
}
