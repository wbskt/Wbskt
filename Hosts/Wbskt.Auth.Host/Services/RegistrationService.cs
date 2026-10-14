using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Auth.Host.Services;

internal sealed class RegistrationService : IRegistrationService
{
    private const string DefaultWorkspaceName = "Default Workspace";

    /// <summary>
    /// One answer for every way an invitation can fail. Registration is anonymous, so distinguishing
    /// "no such token" from "wrong address" would turn it into an oracle for probing invitations.
    /// </summary>
    private static readonly Error InvalidInvitation = SqlGuardErrors.InvalidInvitation;

    private static readonly Error InvalidVerificationToken =
        Error.Validation("VERIFICATION_TOKEN_INVALID", "This confirmation link is not valid. It may have expired or already been used. Request a new one.");

    /// <summary>
    /// Longer than a reset link, deliberately. This one is not a credential for an existing account -
    /// it only ever turns an unusable account into a usable one - and sign-up commonly happens
    /// minutes before someone stops looking at their inbox for the day. An hour here would mean
    /// anyone who registers in the evening finds a dead link in the morning.
    /// </summary>
    private static readonly TimeSpan EmailVerificationLifetime = TimeSpan.FromHours(24);

    private readonly IAccountProvider _accounts;
    private readonly ITenantProvider _tenants;
    private readonly IEventBus _eventBus;
    private readonly IAuthMailer _mailer;
    private readonly MailCooldown _mailCooldown;
    private readonly ILogger<RegistrationService> _logger;
    private readonly AuthMetrics _metrics;
    private readonly IAuditWorkspaces _audit;
    private readonly AccountAnnouncer _announcer;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public RegistrationService(
        IAccountProvider accounts,
        ITenantProvider tenants,
        IEventBus eventBus,
        IAuthMailer mailer,
        ILogger<RegistrationService> logger,
        AuthMetrics metrics,
        MailCooldown mailCooldown,
        IAuditWorkspaces? audit = null)
    {
        _audit = audit ?? AuditWorkspaces.None;
        _accounts = accounts;
        _tenants = tenants;
        _eventBus = eventBus;
        _mailer = mailer;
        _mailCooldown = mailCooldown;
        _logger = logger;
        _metrics = metrics;
        _announcer = new AccountAnnouncer(accounts, eventBus, _audit, logger);
    }

    public async Task<Result> RegisterUserAsync(string username, string email, string password, string? invitationToken = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Attempting to register user: {Username}", username);

        // Checked before the account is created, not after. Redeeming an invitation is the last step
        // of registration, so a token that turns out to be unusable would otherwise leave behind an
        // account the user cannot register again — their email is taken — and did not want on its own.
        InvitationLookup? invitation = null;
        if (invitationToken is not null)
        {
            var validated = await ValidateInvitationAsync(invitationToken, email, cancellationToken);
            if (validated.IsFailure)
            {
                _metrics.RecordRegistration("invalid_invitation");
                return Result.Failure(validated.Error);
            }

            invitation = validated.Value;
        }

        try
        {
            var user = new User
            {
                Username = username,
                Email = email
            };

            user.PasswordHash = _passwordHasher.HashPassword(user, password);

            var userId = await _accounts.InsertUserAsync(user, cancellationToken);
            _logger.LogDebug("User record inserted with database ID: {UserId}", userId);

            // Everyone gets their own tenant, invited or not. It is where a single user's workspaces
            // live, and it means a member who later leaves the tenant that invited them still has
            // somewhere to be — an account belonging to no tenant can do nothing at all and no
            // endpoint can repair it.
            var tenantRefId = await _tenants.CreateTenantAsync($"{username}'s Tenant", null, userId, DefaultWorkspaceName, cancellationToken);
            _logger.LogInformation("Created tenant {TenantRefId} for new user ID {UserId}", tenantRefId, userId);

            int? joinedTenantId = null;
            if (invitationToken is not null)
            {
                // Re-validated authoritatively inside the procedure. Between the check above and here
                // the invitation could have been revoked or redeemed, in which case this throws and
                // registration fails — the account and its own tenant survive, so the user can log in
                // and ask for a fresh invitation rather than being stranded.
                var tenantId = await _tenants.AcceptInvitationAsync(SecurityTokens.Hash(invitationToken), userId, cancellationToken);
                _logger.LogInformation("New user ID {UserId} joined tenant ID {TenantId} by invitation", userId, tenantId);
                joinedTenantId = tenantId;
            }

            user = await _accounts.GetByIdAsync(userId, cancellationToken);

            // The account exists and is unusable until this link is followed. Issued as its own step
            // rather than inside a transaction with the rows above, because there is no ambient
            // transaction here to join - the user insert and Tenant_Create are already separate calls,
            // and Tenant_Create is the only one that is atomic internally. If this step or the mail
            // fails, resend-verification is the way back, which is why it is anonymous.
            //
            // Always sent: the address had no account a moment ago, so nothing has been mailed to
            // it. Still counted, so a resend straight after signing up waits out the cooldown.
            await _mailCooldown.TryAcquireAsync(user.Email, MailKind.EmailVerification);
            await IssueEmailVerificationAsync(user, cancellationToken);

            await _eventBus.PublishAsync(new UserRegisteredEvent(userId, user.RefId, username, email), cancellationToken);
            if (joinedTenantId is { } joined && invitation is not null)
            {
                await _eventBus.PublishAsync(
                    new InvitationAcceptedEvent(invitation.RefId, invitation.Email, userId, user.RefId) { WorkspaceIds = await _audit.OfTenantAsync(joined, cancellationToken) },
                    cancellationToken);
            }
            _logger.LogInformation("User {Username} registered successfully. RefId: {RefId}", username, user.RefId);
            
            _metrics.RecordRegistration("success");
            return Result.Success();
        }
        catch (Exception ex)
        {
            // The invitation was revoked or redeemed between the pre-check and the redemption. The
            // account and its own tenant exist and are usable, so this is the caller's problem to
            // retry with a fresh invitation, not a server fault — 400, matching the pre-check.
            if (ex is SqlException { Number: 50011 })
            {
                _logger.LogWarning("Registration for {Username} completed but the invitation was no longer redeemable", username);
                _metrics.RecordRegistration("invalid_invitation");
                return Result.Failure(InvalidInvitation);
            }

            if (ex is SqlException { Number: 2601 or 2627 })
            {
                return await HandleRegistrationConflictAsync(username, email, ex, cancellationToken);
            }

            // A fault: counted here, logged and answered by GlobalExceptionMiddleware.
            if (ex is not OperationCanceledException)
            {
                _metrics.RecordRegistration("error");
            }

            throw;
        }
    }

    public async Task<Result> VerifyEmailAsync(string token, CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = await _accounts.ConsumeEmailVerificationTokenAsync(SecurityTokens.Hash(token), cancellationToken);
            _logger.LogInformation("Email address confirmed for user {UserId}", userId);
            await _announcer.AnnounceAsync(new EmailVerifiedEvent(userId, await _announcer.RefOfAsync(userId, cancellationToken)), null, cancellationToken);
            return Result.Success();
        }
        catch (SecurityException)
        {
            _logger.LogWarning("Email verification rejected: the presented token is not valid.");
            return Result.Failure(InvalidVerificationToken);
        }
    }

    public async Task<Result> ResendVerificationAsync(string email, CancellationToken cancellationToken = default)
    {
        // Anonymous, and 204 on every path, for the same reason as PasswordService.ForgotPasswordAsync.
        //
        // Anonymous specifically because sign-in requires a verified address: an account that needs
        // this endpoint is by definition one that cannot obtain a token to call an authenticated one.
        User user;
        try
        {
            user = await _accounts.GetByEmailAsync(email, cancellationToken);
        }
        catch (SecurityException)
        {
            return Result.Success();
        }

        if (user.IsEmailVerified || !user.IsActive)
        {
            return Result.Success();
        }

        // Before the token, for the same reason as PasswordService.ForgotPasswordAsync: the link already in the
        // owner's inbox keeps working.
        if (!await _mailCooldown.TryAcquireAsync(user.Email, MailKind.EmailVerification))
        {
            _logger.LogInformation("Email verification for user {UserId} suppressed by the mail cooldown", user.Id);
            _metrics.RecordMailSuppressed(nameof(MailKind.EmailVerification));
            return Result.Success();
        }

        try
        {
            await IssueEmailVerificationAsync(user, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reissue an email verification for user {UserId}", user.Id);
        }

        return Result.Success();
    }

    /// <summary>
    /// Issues a fresh verification token and queues the mail carrying it. Supersedes any token the
    /// account already has, so the most recent link is always the one that works.
    /// </summary>
    private async Task IssueEmailVerificationAsync(User user, CancellationToken cancellationToken)
    {
        var token = SecurityTokens.Generate();
        var expiresAt = DateTime.UtcNow.Add(EmailVerificationLifetime);

        await _accounts.CreateEmailVerificationTokenAsync(user.Id, SecurityTokens.Hash(token), expiresAt, cancellationToken);
        await _mailer.QueueEmailVerificationAsync(user.Email, user.Username, token, expiresAt, cancellationToken);

        _logger.LogInformation("Email verification issued for user {UserId}", user.Id);
    }

    /// <summary>
    /// Answers a uniqueness conflict on registration without saying which address is taken.
    /// </summary>
    /// <remarks>
    /// An address that already has an account gets the same 204 a successful registration gets, and
    /// the fact that it is taken is delivered to the address itself rather than to whoever submitted
    /// the form. A username collision is reported plainly: it says nothing about any address, and
    /// answering 204 there would leave someone stuck retrying a name that will never be accepted.
    /// </remarks>
    /// <remarks>
    /// Internal rather than private only so the suite can exercise it directly: the alternative is
    /// fabricating a <see cref="SqlException"/>, which has no public constructor. <c>RegistrationService</c>
    /// is itself internal, so this widens nothing outside the assembly.
    /// </remarks>
    internal async Task<Result> HandleRegistrationConflictAsync(string username, string email, Exception ex, CancellationToken cancellationToken)
    {
        // The type, not the message: a unique-key violation quotes the duplicate value, the address.
        _logger.LogWarning("User registration conflict for Username={Username}. Error: {ErrorType}", username, ex.GetType().Name);

        User? existing;
        try
        {
            existing = await _accounts.GetByEmailAsync(email, cancellationToken);
        }
        catch (SecurityException)
        {
            // No account on this address, so the collision was on the username.
            existing = null;
        }
        catch (Exception lookupEx) when (lookupEx is not OperationCanceledException)
        {
            // The lookup itself failed, so which field collided is unknown. Reporting a username
            // conflict here would be a guess, and reporting 204 would tell the caller an account was
            // created when none was. This is our fault and reaches the caller as a 500.
            _metrics.RecordRegistration("error");
            throw;
        }

        if (existing is null)
        {
            _metrics.RecordRegistration("conflict");
            return Result.Failure(Error.Conflict("AUTH_USERNAME_CONFLICT", "That username is already taken."));
        }

        try
        {
            if (await _mailCooldown.TryAcquireAsync(existing.Email, MailKind.AccountAlreadyExists))
            {
                await _mailer.QueueAccountAlreadyExistsAsync(existing.Email, existing.Username, cancellationToken);
            }
            else
            {
                _logger.LogInformation("Account-exists notice for user {UserId} suppressed by the mail cooldown", existing.Id);
                _metrics.RecordMailSuppressed(nameof(MailKind.AccountAlreadyExists));
            }
        }
        catch (Exception mailEx)
        {
            _logger.LogError(mailEx, "Failed to notify user {UserId} that their address was used in a registration attempt", existing.Id);
        }

        // Counted separately from "success" so the metric does not claim accounts that were never
        // created, and separately from "conflict" so this path stays visible.
        _metrics.RecordRegistration("existing_address");
        return Result.Success();
    }

    /// <summary>
    /// Confirms a token names a live invitation addressed to <paramref name="email"/>, before any
    /// account exists. Every rejection is reported identically, so an unauthenticated caller cannot
    /// use registration to discover whether a token is real or which address it was issued to.
    /// </summary>
    private async Task<Result<InvitationLookup>> ValidateInvitationAsync(string invitationToken, string email, CancellationToken cancellationToken)
    {
        InvitationLookup invitation;
        try
        {
            invitation = await _tenants.GetInvitationByTokenHashAsync(SecurityTokens.Hash(invitationToken), cancellationToken);
        }
        catch (SecurityException)
        {
            _logger.LogWarning("Registration rejected: no invitation matches the presented token");
            return Result<InvitationLookup>.Failure(InvalidInvitation);
        }

        // Ordinal-ignore-case, matching how SQL Server compares under the database's default
        // collation — so the check here agrees with the one TenantInvitation_Accept applies.
        if (!invitation.IsLive || !string.Equals(invitation.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Registration rejected: invitation {RefId} is not live, or was issued to a different address", invitation.RefId);
            return Result<InvitationLookup>.Failure(InvalidInvitation);
        }

        return Result<InvitationLookup>.Success(invitation);
    }
}
