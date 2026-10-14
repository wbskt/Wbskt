using Wbskt.Auth.Host.Providers;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// Publishes a change a person made to their own account, logged in each of their workspaces. Built by
/// the service that owns the change, from its own bus and logger.
/// </summary>
internal sealed class AccountAnnouncer(IAccountProvider accounts, IEventBus eventBus, IAuditWorkspaces audit, ILogger logger)
{
    /// <summary>Best effort: the change is made, and a failure here must not report it as failed.</summary>
    public async Task AnnounceAsync(AccountEvent @event, string? ipAddress, CancellationToken cancellationToken)
    {
        try
        {
            await eventBus.PublishAsync(
                @event with { WorkspaceIds = await audit.OfUserAsync(@event.UserId, cancellationToken), ClientAddress = ipAddress },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not publish {EventType} for user {UserId} for the audit log.", @event.GetType().Name, @event.UserId);
        }
    }

    /// <summary>The user's reference, or <see cref="Guid.Empty"/> when it cannot be read; only the audit log uses it.</summary>
    public async Task<Guid> RefOfAsync(int userId, CancellationToken cancellationToken)
    {
        try
        {
            return (await accounts.GetByIdAsync(userId, cancellationToken)).RefId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read user {UserId}'s reference for the audit log.", userId);
            return Guid.Empty;
        }
    }
}
