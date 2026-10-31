using Microsoft.AspNetCore.Identity;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
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
    private readonly IEventBus _eventBus;
    private readonly IAccessTokenRevocation _accessTokens;
    private readonly IAuditWorkspaces _audit;
    private readonly ILogger<AccountDeletionService> _logger;
    private readonly TenantAnnouncer _tenantAnnouncer;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public AccountDeletionService(
        IAccountProvider accounts,
        IEventBus eventBus,
        IAccessTokenRevocation accessTokens,
        ILogger<AccountDeletionService> logger,
        IAuditWorkspaces? audit = null)
    {
        _accounts = accounts;
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
