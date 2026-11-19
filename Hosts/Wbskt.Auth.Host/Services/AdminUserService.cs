using Microsoft.Data.SqlClient;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.Auth.Models;
using Wbskt.Events.Auth;
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
/// change is recorded in the staff audit instead. The changes that keep the person from working (signing
/// them out everywhere, turning sign-in off or on) are also told to each workspace they belong to, as a
/// <see cref="SupportActionTakenEvent"/>, so the tenant's administrators can see Wbskt support did it.
/// </remarks>
internal sealed class AdminUserService(
    IAccountProvider accounts,
    IStaffUserProvider staffUsers,
    ISessionProvider sessions,
    IAccessTokenRevocation accessTokens,
    IAuthMailer mailer,
    IStaffActionRunner runner,
    TenantAnnouncer announcer,
    TimeProvider time)
    : IAdminUserService
{
    public const string ClearLockoutAction = "ClearSignInLockout";
    public const string ResendVerificationAction = "ResendEmailVerification";
    public const string SendPasswordResetAction = "SendPasswordReset";
    public const string SignOutEverywhereAction = "SignOutEverywhere";
    public const string AllowSignInAction = "AllowSignIn";
    public const string BlockSignInAction = "BlockSignIn";
    public const string CorrectEmailAction = "CorrectEmail";
    public const string TargetKind = "Person";

    /// <summary>The address a token records as having asked for it or revoked it, for a staff change.</summary>
    internal const string StaffAddress = "staff";

    private static readonly Error UserNotFound = Error.NotFound("USER_NOT_FOUND", "No person has that RefId.");
    private static readonly Error EmailVerified = Error.Conflict("EMAIL_ALREADY_VERIFIED", "This person's address is already verified.");
    private static readonly Error SignInOff = Error.Conflict("USER_SIGN_IN_OFF", "Sign-in is off for this person; turn it on first.");

    public async Task<Result<StaffActionResponse>> ClearLockoutAsync(Guid userRef, AdminUserActionRequest request, CancellationToken cancellationToken)
    {
        var user = await FindAsync(userRef, cancellationToken);
        if (user is null)
        {
            return UserNotFound;
        }

        return await RunAsync(request, ClearLockoutAction, user, async (now, ct) =>
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
        return await RunAsync(request, ResendVerificationAction, user, async (now, ct) =>
        {
            if (now.IsEmailVerified)
            {
                return Result.Failure(EmailVerified);
            }

            if (!now.IsActive)
            {
                return Result.Failure(SignInOff);
            }

            await accounts.CreateEmailVerificationTokenAsync(now.Id, SecurityTokens.Hash(token), expiresAt, ct);
            return Result.Success();
        }, [VerificationMail(user.Email, user, token, expiresAt)], cancellationToken);
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
        return await RunAsync(request, SendPasswordResetAction, user, async (now, ct) =>
        {
            if (!now.IsActive)
            {
                return Result.Failure(SignInOff);
            }

            await accounts.CreatePasswordResetTokenAsync(now.Id, SecurityTokens.Hash(token), expiresAt, StaffAddress, ct);
            return Result.Success();
        }, [new AfterCommit("password reset", ct => mailer.QueuePasswordResetAsync(user.Email, user.Username, token, expiresAt, ct).AsTask())], cancellationToken);
    }

    public async Task<Result<StaffActionResponse>> SignOutEverywhereAsync(Guid userRef, AdminUserActionRequest request, CancellationToken cancellationToken)
    {
        var user = await FindAsync(userRef, cancellationToken);
        if (user is null)
        {
            return UserNotFound;
        }

        return await RunAsync(request, SignOutEverywhereAction, user, async (now, ct) =>
        {
            await sessions.RevokeAllRefreshTokensForUserAsync(now.Id, StaffAddress, ct);
            return Result.Success();
        }, [RevokeAccessTokens(user), Announce(SignOutEverywhereAction, user)], cancellationToken);
    }

    public async Task<Result<StaffActionResponse>> SetSignInAsync(Guid userRef, AdminSetUserSignInRequest request, CancellationToken cancellationToken)
    {
        var user = await FindAsync(userRef, cancellationToken);
        if (user is null)
        {
            return UserNotFound;
        }

        var action = request.Allowed ? AllowSignInAction : BlockSignInAction;
        IReadOnlyList<AfterCommit> afterCommit = request.Allowed
            ? [Announce(action, user)]
            : [RevokeAccessTokens(user), Announce(action, user)];
        return await RunAsync(request, action, user, async (now, ct) =>
        {
            if (now.IsActive == request.Allowed)
            {
                return Result.Failure(Error.Conflict("USER_SIGN_IN_UNCHANGED", request.Allowed ? "Sign-in is already on for this person." : "Sign-in is already off for this person."));
            }

            await staffUsers.SetActiveAsync(now.Id, request.Allowed, StaffAddress, ct);
            return Result.Success();
        }, afterCommit, cancellationToken);
    }

    public async Task<Result<StaffActionResponse>> CorrectEmailAsync(Guid userRef, AdminCorrectUserEmailRequest request, CancellationToken cancellationToken)
    {
        var user = await FindAsync(userRef, cancellationToken);
        if (user is null)
        {
            return UserNotFound;
        }

        var email = request.Email.Trim();
        var token = SecurityTokens.Generate();
        var expiresAt = time.GetUtcNow().UtcDateTime.Add(RegistrationService.EmailVerificationLifetime);

        // The addresses stay out of the audit row, which outlives the account.
        var action = new StaffAction(CorrectEmailAction, TargetKind, user.RefId);
        return await runner.RunAsync(request, action, async ct =>
        {
            var now = await accounts.FindByIdAsync(user.Id, ct);
            if (now is null)
            {
                return Result.Failure(UserNotFound);
            }

            if (now.IsEmailVerified)
            {
                return Result.Failure(EmailVerified);
            }

            if (!now.IsActive)
            {
                return Result.Failure(SignInOff);
            }

            if (string.Equals(now.Email, email, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure(Error.Conflict("EMAIL_UNCHANGED", "That is already the person's address. Resend the verification link instead."));
            }

            try
            {
                await staffUsers.CorrectUnverifiedEmailAsync(now.Id, email, SecurityTokens.Hash(token), expiresAt, ct);
                return Result.Success();
            }
            catch (SqlException ex) when (ex.Number is 50046)
            {
                return Result.Failure(EmailVerified);
            }
            catch (SqlException ex) when (ex.Number is 50047 or 2601 or 2627)
            {
                return Result.Failure(Error.Conflict("EMAIL_IN_USE", "Another account uses that address."));
            }
        }, [VerificationMail(email, user, token, expiresAt)], cancellationToken);
    }

    private AfterCommit Announce(string action, User user) =>
        new("audit event", ct => announcer.ToUserAsync(new SupportActionTakenEvent(action, TargetKind, user.RefId), user.Id, ct));

    private AfterCommit RevokeAccessTokens(User user) =>
        new("access token revocation", ct => accessTokens.RevokeUserAsync(user.RefId, ct));

    private AfterCommit VerificationMail(string email, User user, string token, DateTime expiresAt) =>
        new("email verification", ct => mailer.QueueEmailVerificationAsync(email, user.Username, token, expiresAt, ct).AsTask());

    /// <summary>
    /// Checks the person's state inside the runner, after its replay check, so a retry of a change that
    /// already happened gets the first answer rather than "nothing to do". The person is read again in the
    /// change's transaction; a refusal there is audited as Refused.
    /// </summary>
    private Task<Result<StaffActionResponse>> RunAsync(IStaffRequest request, string action, User user, Func<User, CancellationToken, Task<Result>> change, CancellationToken cancellationToken) =>
        RunAsync(request, action, user, change, [], cancellationToken);

    private Task<Result<StaffActionResponse>> RunAsync(IStaffRequest request, string action, User user, Func<User, CancellationToken, Task<Result>> change, IReadOnlyList<AfterCommit> afterCommit, CancellationToken cancellationToken) =>
        runner.RunAsync(request, new StaffAction(action, TargetKind, user.RefId), async ct =>
            await accounts.FindByIdAsync(user.Id, ct) is { } now ? await change(now, ct) : Result.Failure(UserNotFound), afterCommit, cancellationToken);

    private async Task<User?> FindAsync(Guid userRef, CancellationToken cancellationToken)
    {
        return await accounts.FindIdByRefIdAsync(userRef, cancellationToken) is { } id ? await accounts.FindByIdAsync(id, cancellationToken) : null;
    }
}
