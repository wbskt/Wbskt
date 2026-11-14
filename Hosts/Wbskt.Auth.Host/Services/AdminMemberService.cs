using Microsoft.Data.SqlClient;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Models;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure.StaffActions;
using Wbskt.Infrastructure;
using Wbskt.Models.Admin;

namespace Wbskt.Auth.Host.Services;

/// <remarks>
/// The staff variants of the console's suspend and remove, on the same procedures and their guards: a
/// tenant is never left without an administrator. Removal is the leave procedure acting for the member,
/// so their workspaces pass to another administrator of the tenant rather than to a caller, who has none
/// here. Membership is checked by the procedures, after the runner's idempotency lookup, so a retry of a
/// change already made replays it. The events go out once the change has committed, and not for a replay.
/// The audit row is on the account, with the member in its detail.
/// </remarks>
internal sealed class AdminMemberService(
    IStaffUserProvider staff,
    ITenantProvider tenants,
    IAccountProvider accounts,
    IEventBus eventBus,
    IAuditWorkspaces audit,
    IStaffActionRunner runner,
    ILogger<AdminMemberService> logger)
    : IAdminMemberService
{
    public const string SuspendAction = "SuspendMember";
    public const string RestoreAction = "RestoreMember";
    public const string RemoveAction = "RemoveMember";
    public const string TargetKind = "Account";

    private static readonly Error TenantNotFound = Error.NotFound("TENANT_NOT_FOUND", "No tenant has that RefId.");
    private static readonly Error MemberNotFound = Error.NotFound("MEMBER_NOT_FOUND", "That person is not a member of this tenant.");
    private static readonly Error LastAdministrator = Error.Conflict("TENANT_LAST_ADMINISTRATOR", "This would leave the tenant without an administrator. Make another member an administrator first.");

    private readonly TenantAnnouncer _announcer = new(accounts, eventBus, audit, logger);

    public async Task<Result<StaffActionResponse>> SetSuspendedAsync(Guid tenantRef, Guid userRef, AdminSetMemberSuspendedRequest request, CancellationToken cancellationToken)
    {
        var target = await FindAsync(tenantRef, userRef, cancellationToken);
        if (target.IsFailure)
        {
            return target.Error;
        }

        var (tenantId, user) = target.Value;
        var result = await runner.RunAsync(request.Staff, Action(request.Suspended ? SuspendAction : RestoreAction, tenantRef, user), async ct =>
        {
            try
            {
                await tenants.SetMemberSuspendedAsync(tenantId, user.Id, request.Suspended, ct);
                return Result.Success();
            }
            catch (SqlException ex) when (ex.Number is 50016)
            {
                return Result.Failure(MemberNotFound);
            }
            catch (SqlException ex) when (ex.Number is 50008)
            {
                return Result.Failure(LastAdministrator);
            }
        }, cancellationToken);

        await AfterCommitAsync(result, async () =>
        {
            await _announcer.UserPermissionsChangedAsync(user.Id, CancellationToken.None);
            await _announcer.ToTenantAsync(request.Suspended ? new MemberSuspendedEvent(userRef) : new MemberUnsuspendedEvent(userRef), tenantId, CancellationToken.None);
            if (request.Suspended)
            {
                await eventBus.PublishAsync(new SecurityAlertEvent(
                    "MemberSuspended",
                    $"User ID {user.Id} was suspended in tenant ID {tenantId} by staff member {request.Staff.StaffName.Trim()}.",
                    AdminUserService.StaffAddress,
                    $"UserId: {user.Id}, TenantId: {tenantId}"), CancellationToken.None);
            }
        });
        return result;
    }

    public async Task<Result<StaffActionResponse>> RemoveAsync(Guid tenantRef, Guid userRef, AdminRemoveMemberRequest request, CancellationToken cancellationToken)
    {
        var target = await FindAsync(tenantRef, userRef, cancellationToken);
        if (target.IsFailure)
        {
            return target.Error;
        }

        var (tenantId, user) = target.Value;
        var result = await runner.RunAsync(request.Staff, Action(RemoveAction, tenantRef, user), async ct =>
        {
            try
            {
                await tenants.LeaveTenantAsync(tenantRef, user.Id, ct);
                return Result.Success();
            }
            catch (SqlException ex) when (RemoveRefusal(ex) is { } refusal)
            {
                return Result.Failure(refusal);
            }
        }, cancellationToken);

        await AfterCommitAsync(result, async () =>
        {
            await _announcer.UserPermissionsChangedAsync(user.Id, CancellationToken.None);
            await _announcer.ToTenantAsync(new MemberRemovedEvent(userRef), tenantId, CancellationToken.None);
        });
        return result;
    }

    private static Error? RemoveRefusal(SqlException ex) => ex.Number switch
    {
        50023 => MemberNotFound,
        50024 => Error.Conflict("MEMBER_ONLY_TENANT", "This is the only tenant the person belongs to. An account in no tenant can do nothing, so they cannot be removed from it."),
        50025 => Error.Conflict("TENANT_SOLE_MEMBER", "The person is the only member of this tenant, so removing them would leave it with nobody."),
        50026 => LastAdministrator,
        _ => null
    };

    private async Task<Result<(int TenantId, User User)>> FindAsync(Guid tenantRef, Guid userRef, CancellationToken cancellationToken)
    {
        int tenantId = await staff.FindTenantIdByRefIdAsync(tenantRef, cancellationToken);
        if (tenantId == 0)
        {
            return TenantNotFound;
        }

        int userId = await accounts.FindIdByRefIdAsync(userRef, cancellationToken);
        if (userId == 0)
        {
            return MemberNotFound;
        }

        return (tenantId, await accounts.GetByIdAsync(userId, cancellationToken));
    }

    private static StaffAction Action(string name, Guid tenantRef, User user) =>
        new(name, TargetKind, tenantRef, $"Member {user.Username} ({user.RefId})");

    /// <summary>
    /// Runs <paramref name="effect"/> once the change has committed, and not for a replay. The change is made
    /// and audited, so a failure here is logged rather than reported as the change failing.
    /// </summary>
    private async Task AfterCommitAsync(Result<StaffActionResponse> result, Func<Task> effect)
    {
        if (result.IsFailure || result.Value.Replayed)
        {
            return;
        }

        try
        {
            await effect();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "A staff change to a tenant's member committed, but announcing it failed.");
        }
    }
}
