using Wbskt.Auth.Models;
using Wbskt.Infrastructure.Billing;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services.Billing;

/// <summary>
/// Copies workspace accounts (each workspace with its tenant's plan) from the auth host, which owns
/// them, into the main database (<c>dbo.WorkspaceAccounts</c>), where the hosts that enforce plan
/// limits read them. Every copy is taken from the auth host as it stands, never from an event, and is
/// written only over an older revision, so the callers can run in any order and any number of times.
/// </summary>
public sealed class WorkspaceAccountSync(
    IWorkspaceAccountsClient auth,
    IWorkspaceAccountProvider accounts,
    IWorkspacePlans plans,
    ILogger<WorkspaceAccountSync> logger)
{
    /// <summary>Every workspace: the periodic sync that repairs whatever an event did not reach.</summary>
    public Task<int> SyncAllAsync(CancellationToken ct) => SyncAsync(tenantId: null, workspaceId: null, ct);

    /// <summary>Every workspace of one tenant, after its plan changed.</summary>
    public Task<int> SyncTenantAsync(int tenantId, CancellationToken ct) => SyncAsync(tenantId, workspaceId: null, ct);

    /// <summary>One workspace, as soon as it is created, so it is not on Free until the next full sync.</summary>
    public Task<int> SyncWorkspaceAsync(int workspaceId, CancellationToken ct) => SyncAsync(tenantId: null, workspaceId, ct);

    /// <summary>Returns how many copies were inserted or changed.</summary>
    private async Task<int> SyncAsync(int? tenantId, int? workspaceId, CancellationToken ct)
    {
        int changed = 0;
        string? cursor = null;
        do
        {
            Page<WorkspaceAccountResponse> page = await auth.GetPageAsync(cursor, tenantId, workspaceId, ct);
            WorkspaceAccount[] copies = page.Items.Select(ToAccount).ToArray();
            changed += await accounts.UpsertAsync(copies, ct);
            foreach (WorkspaceAccount copy in copies)
            {
                plans.Invalidate(copy.WorkspaceId);
            }

            cursor = page.NextCursor;
        }
        while (cursor is not null);

        if (changed > 0)
        {
            logger.LogInformation(
                "Workspace accounts synced (tenant {TenantId}, workspace {WorkspaceId}): {Changed} copy(ies) inserted or changed",
                tenantId, workspaceId, changed);
        }

        return changed;
    }

    private static WorkspaceAccount ToAccount(WorkspaceAccountResponse response) =>
        new(response.WorkspaceId, response.TenantId, response.TenantRefId, response.PlanCode, response.LimitOverrides, response.PlanRevision);
}
