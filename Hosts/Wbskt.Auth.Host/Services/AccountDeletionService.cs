using Microsoft.AspNetCore.Identity;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services.Billing;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Events.Outbox;
using Wbskt.Infrastructure.Security;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Auth.Host.Services;

internal sealed class AccountDeletionService : IAccountDeletionService
{
    private readonly IAccountProvider _accounts;
    private readonly ITenantBillingProvider _billing;
    private readonly IPaymentGateway _gateway;
    private readonly IEventBus _eventBus;
    private readonly IAccessTokenRevocation _accessTokens;
    private readonly IAuditWorkspaces _audit;
    private readonly ILogger<AccountDeletionService> _logger;
    private readonly TenantAnnouncer _tenantAnnouncer;
    private readonly PasswordHasher<User> _passwordHasher = new();

    private static readonly Error SubscriptionNotCancelled = Error.Unavailable(
        "BILLING_SUBSCRIPTION_NOT_CANCELLED",
        "Your subscription could not be cancelled with the payment provider, so nothing was deleted. Try again shortly.");

    public AccountDeletionService(
        IAccountProvider accounts,
        ITenantBillingProvider billing,
        IPaymentGateway gateway,
        IEventBus eventBus,
        IAccessTokenRevocation accessTokens,
        ILogger<AccountDeletionService> logger,
        IAuditWorkspaces? audit = null)
    {
        _accounts = accounts;
        _billing = billing;
        _gateway = gateway;
        _eventBus = eventBus;
        _accessTokens = accessTokens;
        _audit = audit ?? AuditWorkspaces.None;
        _logger = logger;
        _tenantAnnouncer = new TenantAnnouncer(accounts, eventBus, _audit, logger);
    }

    public async Task<Result> DeleteAccountAsync(int userId, string password, string ipAddress, CancellationToken cancellationToken = default)
    {
        User user;
        try
        {
            user = await _accounts.GetByIdAsync(userId, cancellationToken);
        }
        catch (SecurityException)
        {
            return Result.Failure(Error.Unauthorized("AUTH_USER_NOT_FOUND", "User not found."));
        }

        // Deactivated by an operator, or already deleted (a retry after a response that never arrived):
        // either way there is nothing this person may delete.
        if (!user.IsActive)
        {
            return Result.Failure(Error.Unauthorized("AUTH_USER_NOT_FOUND", "User not found."));
        }

        // Same rules as changing the password: a locked account is not asked, and a wrong password
        // counts towards the lock, so a stolen access token cannot guess its way to deleting anything.
        if (user.LockedUntil > DateTime.UtcNow)
        {
            _logger.LogWarning("Account deletion refused: account {UserId} is locked. IP: {IpAddress}", userId, ipAddress);
            return Result.Failure(Error.Forbidden("AUTH_ACCOUNT_LOCKED", "Too many wrong passwords. Try again later, or reset your password."));
        }

        if (_passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Account deletion refused: password did not match for user {UserId}. IP: {IpAddress}", userId, ipAddress);
            await AccountLockout.RecordFailedPasswordAsync(_accounts, _eventBus, _logger, user, ipAddress, cancellationToken);
            return Result.Failure(Error.Validation("AUTH_CURRENT_PASSWORD_INVALID", "The password is not correct."));
        }

        // Asked before the provider is: a deletion User_Delete would refuse must not cancel the
        // subscriptions first. User_Delete checks again under its locks for a change in between.
        var blockers = await _accounts.GetDeletionBlockersAsync(userId, cancellationToken);
        if (blockers.Count > 0)
        {
            _logger.LogInformation("Account deletion refused: user {UserId} is the only administrator of {TenantCount} shared tenant(s).", userId, blockers.Count);
            return Result.Failure(SqlGuardErrors.SoleAdministrator(blockers));
        }

        var cancelled = await CancelSubscriptionsAsync(userId, cancellationToken);
        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        var workspaceIds = await _accounts.GetSoleTenantWorkspaceIdsAsync(userId, cancellationToken);
        var deletedWorkspaces = workspaceIds.ToDictionary(id => id, id => OutboxMessage.Create(new WorkspaceDeletedEvent(id, userId)));

        // Read before the memberships go; the shared tenants' workspaces are where the deletion is logged.
        var auditWorkspaces = await _audit.OfUserAsync(userId, cancellationToken);

        var deleted = await SqlGuardErrors.GuardAsync(_logger, "Delete account", () => _accounts.DeleteUserAsync(userId, deletedWorkspaces, cancellationToken));
        if (deleted.IsFailure)
        {
            return Result.Failure(deleted.Error);
        }

        // The refresh tokens went with the account; this ends the access tokens already issued, on
        // every host. RefId is untouched by the anonymisation, so it still names them.
        await _accessTokens.RevokeUserAsync(user.RefId, cancellationToken);

        await AnnounceAsync(user, deleted.Value, auditWorkspaces.Except(workspaceIds).ToList(), ipAddress, cancellationToken);

        _logger.LogInformation("Account {UserId} deleted by its owner, with {WorkspaceCount} workspaces. IP: {IpAddress}", userId, workspaceIds.Count, ipAddress);
        return Result.Success();
    }

    /// <summary>
    /// Cancels, with the payment provider, every subscription of the tenants this deletion deletes, so a
    /// deleted account is never charged again. When the provider cannot be reached nothing is deleted:
    /// the caller tries again.
    /// </summary>
    private async Task<Result> CancelSubscriptionsAsync(int userId, CancellationToken cancellationToken)
    {
        var live = await _billing.GetLiveSubscriptionsOfSoleTenantsAsync(userId, cancellationToken);
        foreach (var subscription in live)
        {
            if (!_gateway.IsConfigured || !string.Equals(subscription.Provider, _gateway.Name, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("Account deletion refused: user {UserId}'s tenant {TenantId} has subscription {SubscriptionId} with {Provider}, which cannot be cancelled here.",
                    userId, subscription.TenantId, subscription.SubscriptionId, subscription.Provider);
                return Result.Failure(SubscriptionNotCancelled);
            }

            try
            {
                await _gateway.CancelSubscriptionAsync(subscription.SubscriptionId, cancellationToken);
                _logger.LogInformation("Cancelled subscription {SubscriptionId} of tenant {TenantId} before deleting account {UserId}.", subscription.SubscriptionId, subscription.TenantId, userId);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Account deletion refused: could not cancel subscription {SubscriptionId} of tenant {TenantId} for user {UserId}.", subscription.SubscriptionId, subscription.TenantId, userId);
                return Result.Failure(SubscriptionNotCancelled);
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// What leaving each shared tenant would have announced through the members endpoint, and the
    /// deletion itself. Best effort: the account is gone, and a failure here must not say otherwise.
    /// </summary>
    private async Task AnnounceAsync(User user, IReadOnlyCollection<int> leftTenantIds, IReadOnlyList<int> auditWorkspaces, string ipAddress, CancellationToken cancellationToken)
    {
        try
        {
            await _eventBus.PublishAsync(new UserPermissionsChangedEvent(user.Id, user.RefId), cancellationToken);
            foreach (var tenantId in leftTenantIds)
            {
                await _tenantAnnouncer.ToTenantAsync(new MemberRemovedEvent(user.RefId), tenantId, cancellationToken);
            }

            await _eventBus.PublishAsync(new AccountDeletedEvent(user.Id, user.RefId) { WorkspaceIds = auditWorkspaces, ClientAddress = ipAddress }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not announce the deletion of account {UserId}.", user.Id);
        }
    }
}
