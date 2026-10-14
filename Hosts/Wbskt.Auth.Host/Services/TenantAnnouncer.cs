using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Auth;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// Publishes what tenant administration changed: to the hosts that cache a user's effective
/// permissions, and to the audit log of every workspace in the tenant. Built by the service that owns
/// the change, from its own bus and logger.
/// </summary>
internal sealed class TenantAnnouncer(IAccountProvider accounts, IEventBus eventBus, IAuditWorkspaces audit, ILogger logger)
{
    /// <summary>Tells the permission caches that the user's effective set may have changed, and returns the user.</summary>
    public async Task<User> UserPermissionsChangedAsync(int userId, CancellationToken cancellationToken)
    {
        var user = await accounts.GetByIdAsync(userId, cancellationToken);
        await eventBus.PublishAsync(new UserPermissionsChangedEvent(userId, user.RefId), cancellationToken);
        return user;
    }

    /// <summary>Publishes an action on the tenant's people, to be logged in each of its workspaces.</summary>
    /// <remarks>
    /// The action has already happened, so a failure to publish is logged rather than returned:
    /// reporting it as failed would only invite a retry of something that succeeded.
    /// </remarks>
    public async Task ToTenantAsync(TenantActorEvent @event, int tenantId, CancellationToken cancellationToken)
    {
        try
        {
            await eventBus.PublishAsync(@event with { WorkspaceIds = await audit.OfTenantAsync(tenantId, cancellationToken) }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not publish {EventType} for the audit log.", @event.GetType().Name);
        }
    }
}
