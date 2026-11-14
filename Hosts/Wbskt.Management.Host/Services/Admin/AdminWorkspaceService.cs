using Wbskt.Auth.Models;
using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.StaffActions;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Billing;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Models;
using Wbskt.Models.Admin;

namespace Wbskt.Management.Host.Services.Admin;

/// <remarks>
/// The cap goes through <see cref="WorkspaceCreditCapService"/>, the service the console's route uses,
/// so a staff change takes the same validation, locking and event. Its event is held until the staff
/// runner has committed, and sent only for a change made now, not for a replay. The main database knows
/// a workspace only by its Id, so the RefId is looked up on the auth host first, outside the change.
/// </remarks>
internal sealed class AdminWorkspaceService(
    IWorkspaceAccountsClient workspaces,
    IWorkspaceCreditCapProvider capProvider,
    IEventBus eventBus,
    TimeProvider clock,
    IStaffActionRunner runner,
    ILogger<AdminWorkspaceService> logger)
    : IAdminWorkspaceService
{
    public const string SetCreditCapAction = "SetWorkspaceCreditCap";
    public const string TargetKind = "Workspace";

    public async Task<Result<StaffActionResponse>> SetCreditCapAsync(Guid workspaceRef, AdminSetWorkspaceCreditCapRequest request, CancellationToken cancellationToken)
    {
        WorkspaceAccountResponse? workspace;
        try
        {
            workspace = await workspaces.FindAsync(workspaceRef, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Nothing was done, so the admin can send the same request again.
            logger.LogWarning(ex, "The auth host did not answer the lookup of workspace {WorkspaceRef}.", workspaceRef);
            return Error.Unavailable("AUTH_HOST_UNAVAILABLE", "The auth host did not answer. Try again: nothing was changed.");
        }

        if (workspace is null)
        {
            return Error.NotFound("WORKSPACE_NOT_FOUND", "No workspace has that RefId.");
        }

        var held = new DeferredEventBus();
        var caps = new WorkspaceCreditCapService(capProvider, held, clock);
        var result = await runner.RunAsync(
            request.Staff,
            new StaffAction(SetCreditCapAction, TargetKind, workspaceRef),
            async ct =>
            {
                var set = await caps.SetAsync(workspace.WorkspaceId, new WorkspaceCreditCapRequest(request.MonthlyCreditCap), ct);
                return set.IsSuccess ? Result.Success() : Result.Failure(set.Error);
            },
            cancellationToken);

        if (result.IsSuccess && !result.Value.Replayed)
        {
            await held.ReleaseAsync(eventBus, cancellationToken);
        }

        return result;
    }
}
