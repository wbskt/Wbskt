using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Infrastructure.StaffActions;
using Wbskt.Models.Admin;

namespace Wbskt.Auth.Host.Services;

/// <remarks>
/// Each change runs inside the staff runner's transaction with its audit row. What cannot be rolled back,
/// a mail or the access-token watermark, happens after the runner has committed, and only for a change
/// made now: a replayed request already sent its mail the first time. The person's own audit events
/// (<c>PasswordResetRequestedEvent</c>, <c>SessionsRevokedEvent</c>) say the person acted, so a staff
/// change is recorded in the staff audit instead.
/// </remarks>
internal sealed class AdminUserService(
    IAccountProvider accounts,
    IStaffUserProvider staffUsers,
    ISessionProvider sessions,
    IAccessTokenRevocation accessTokens,
    IAuthMailer mailer,
    IStaffActionRunner runner,
    TimeProvider time,
    ILogger<AdminUserService> logger)
    : IAdminUserService
{
    public const string ClearLockoutAction = "ClearSignInLockout";
    public const string ResendVerificationAction = "ResendEmailVerification";
    public const string SendPasswordResetAction = "SendPasswordReset";
    public const string SignOutEverywhereAction = "SignOutEverywhere";
    public const string AllowSignInAction = "AllowSignIn";
    public const string BlockSignInAction = "BlockSignIn";
    public const string TargetKind = "Person";

    /// <summary>The address a token records as having asked for it or revoked it, for a staff change.</summary>
    internal const string StaffAddress = "staff";

    private static readonly Error UserNotFound = Error.NotFound("USER_NOT_FOUND", "No person has that RefId.");
    private static readonly Error SignInOff = Error.Conflict("USER_SIGN_IN_OFF", "Sign-in is off for this person; turn it on first.");

    public async Task<Result<StaffActionResponse>> ClearLockoutAsync(Guid userRef, AdminUserActionRequest request, CancellationToken cancellationToken)
    {
        var user = await FindAsync(userRef, cancellationToken);
        if (user is null)
        {
            return UserNotFound;
        }

        return await RunAsync(request.Staff, ClearLockoutAction, user, async (now, ct) =>
        {
            if (now.LockedUntil is not { } until || until <= time.GetUtcNow().UtcDateTime)
            {
                return Result.Failure(Error.Conflict("USER_NOT_LOCKED", "This person is not locked out."));
            }

            await staffUsers.ClearLockoutAsync(now.Id, ct);
            return Result.Success();
        }, cancellationToken);
    }

    public async Task<Result<StaffActionResponse>> ResendVerificationAsync(Guid userRef, AdminUserActionRequest request, CancellationToken cancellationToken)
    {
        var user = await FindAsync(userRef, cancellationToken);
        if (user is null)
        {
            return UserNotFound;
        }

        var token = SecurityTokens.Generate();
        var expiresAt = time.GetUtcNow().UtcDateTime.Add(RegistrationService.EmailVerificationLifetime);
        var result = await RunAsync(request.Staff, ResendVerificationAction, user, async (now, ct) =>
        {
            if (now.IsEmailVerified)
            {
                return Result.Failure(Error.Conflict("EMAIL_ALREADY_VERIFIED", "This person's address is already verified."));
            }

            if (!now.IsActive)
            {
                return Result.Failure(SignInOff);
            }

            await accounts.CreateEmailVerificationTokenAsync(now.Id, SecurityTokens.Hash(token), expiresAt, ct);
            return Result.Success();
        }, cancellationToken);

        await AfterCommitAsync(result, "email verification", user, () => mailer.QueueEmailVerificationAsync(user.Email, user.Username, token, expiresAt, CancellationToken.None).AsTask());
        return result;
    }

    public async Task<Result<StaffActionResponse>> SendPasswordResetAsync(Guid userRef, AdminUserActionRequest request, CancellationToken cancellationToken)
    {
        var user = await FindAsync(userRef, cancellationToken);
        if (user is null)
        {
            return UserNotFound;
        }

        var token = SecurityTokens.Generate();
        var expiresAt = time.GetUtcNow().UtcDateTime.Add(PasswordService.PasswordResetLifetime);
        var result = await RunAsync(request.Staff, SendPasswordResetAction, user, async (now, ct) =>
        {
            if (!now.IsActive)
            {
                return Result.Failure(SignInOff);
            }

            await accounts.CreatePasswordResetTokenAsync(now.Id, SecurityTokens.Hash(token), expiresAt, StaffAddress, ct);
            return Result.Success();
        }, cancellationToken);

        await AfterCommitAsync(result, "password reset", user, () => mailer.QueuePasswordResetAsync(user.Email, user.Username, token, expiresAt, CancellationToken.None).AsTask());
        return result;
    }

    public async Task<Result<StaffActionResponse>> SignOutEverywhereAsync(Guid userRef, AdminUserActionRequest request, CancellationToken cancellationToken)
    {
        var user = await FindAsync(userRef, cancellationToken);
        if (user is null)
        {
            return UserNotFound;
        }

        var result = await RunAsync(request.Staff, SignOutEverywhereAction, user, async (now, ct) =>
        {
            await sessions.RevokeAllRefreshTokensForUserAsync(now.Id, StaffAddress, ct);
            return Result.Success();
        }, cancellationToken);

        await AfterCommitAsync(result, "access token revocation", user, () => accessTokens.RevokeUserAsync(user.RefId, CancellationToken.None));
        return result;
    }

    public async Task<Result<StaffActionResponse>> SetSignInAsync(Guid userRef, AdminSetUserSignInRequest request, CancellationToken cancellationToken)
    {
        var user = await FindAsync(userRef, cancellationToken);
        if (user is null)
        {
            return UserNotFound;
        }

        var result = await RunAsync(request.Staff, request.Allowed ? AllowSignInAction : BlockSignInAction, user, async (now, ct) =>
        {
            if (now.IsActive == request.Allowed)
            {
                return Result.Failure(Error.Conflict("USER_SIGN_IN_UNCHANGED", request.Allowed ? "Sign-in is already on for this person." : "Sign-in is already off for this person."));
            }

            await staffUsers.SetActiveAsync(now.Id, request.Allowed, StaffAddress, ct);
            return Result.Success();
        }, cancellationToken);

        if (!request.Allowed)
        {
            await AfterCommitAsync(result, "access token revocation", user, () => accessTokens.RevokeUserAsync(user.RefId, CancellationToken.None));
        }

        return result;
    }

    /// <summary>
    /// Checks the person's state inside the runner, after its replay check, so a retry of a change that
    /// already happened gets the first answer rather than "nothing to do". The person is read again in the
    /// change's transaction; a refusal there is audited as Refused.
    /// </summary>
    private Task<Result<StaffActionResponse>> RunAsync(StaffChange staff, string action, User user, Func<User, CancellationToken, Task<Result>> change, CancellationToken cancellationToken) =>
        runner.RunAsync(staff, new StaffAction(action, TargetKind, user.RefId), async ct => await change(await accounts.GetByIdAsync(user.Id, ct), ct), cancellationToken);

    private async Task<User?> FindAsync(Guid userRef, CancellationToken cancellationToken)
    {
        int id = await accounts.FindIdByRefIdAsync(userRef, cancellationToken);
        return id == 0 ? null : await accounts.GetByIdAsync(id, cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="effect"/> once the change has committed, and not for a replay. Best effort: the
    /// change is made and audited, so a failure here is logged rather than reported as the change failing.
    /// Effects take no request token, so a caller that goes away after the commit cannot cut them short.
    /// </summary>
    private async Task AfterCommitAsync(Result<StaffActionResponse> result, string what, User user, Func<Task> effect)
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
            logger.LogError(ex, "The staff change to user {UserId} committed, but its {What} failed.", user.Id, what);
        }
    }
}
