using Microsoft.Data.SqlClient;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Models;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.StaffActions;
using Wbskt.Models.Admin;

namespace Wbskt.Auth.Host.Services;

/// <remarks>
/// The freeze itself is a flag on the tenant. What it stops follows from it: workspace access resolves
/// as frozen, so every workspace refuses changes, and once the change has committed the access change is
/// announced (dropping the management host's cached access) and <see cref="TenantFrozenEvent"/> goes to
/// every workspace of the tenant, which the management host acts on to cancel runs and revoke clients.
/// Nothing is announced for a refusal or a replay.
/// </remarks>
internal sealed class AdminFreezeService(
    IStaffUserProvider staff,
    IStaffActionRunner runner,
    TenantAnnouncer announcer)
    : IAdminFreezeService
{
    public const string FreezeAction = "FreezeAccount";
    public const string UnfreezeAction = "UnfreezeAccount";
    public const string TargetKind = "Account";

    private static readonly Error TenantNotFound = Error.NotFound("TENANT_NOT_FOUND", "No tenant has that RefId.");

    public async Task<Result<StaffActionResponse>> SetFrozenAsync(Guid tenantRef, AdminSetTenantFrozenRequest request, CancellationToken cancellationToken)
    {
        int tenantId = await staff.FindTenantIdByRefIdAsync(tenantRef, cancellationToken);
        if (tenantId == 0)
        {
            return TenantNotFound;
        }

        var action = new StaffAction(request.Frozen ? FreezeAction : UnfreezeAction, TargetKind, tenantRef);
        return await runner.RunAsync(request.Staff, action, async ct =>
        {
            try
            {
                await staff.SetTenantFrozenAsync(tenantId, request.Frozen, ct);
                return Result.Success();
            }
            catch (SqlException ex) when (ex.Number is 50049)
            {
                return Result.Failure(request.Frozen
                    ? Error.Conflict("ACCOUNT_ALREADY_FROZEN", "This account is already frozen.")
                    : Error.Conflict("ACCOUNT_NOT_FROZEN", "This account is not frozen."));
            }
        }, [new AfterCommit("announcement", async ct =>
        {
            announcer.AccessChanged();
            await announcer.ToTenantAsync(
                request.Frozen ? new TenantFrozenEvent(tenantRef) : new TenantUnfrozenEvent(tenantRef),
                tenantId,
                ct);
        })], cancellationToken);
    }
}
