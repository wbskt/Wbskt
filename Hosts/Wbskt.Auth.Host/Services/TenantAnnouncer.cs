using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Auth;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// Publishes what tenant administration changed, once it has committed: that a user's or a role's
/// permissions changed, and an action on the tenant's people for the audit log of every workspace in
/// the tenant. It is given the queued bus (<c>AddScopedWithQueuedEvents</c>), so a broker that is down
/// delays these events instead of failing a request whose change has already happened, which would
/// also skip the access-change announcement that follows a success.
/// </summary>
internal sealed class TenantAnnouncer(IAccountProvider accounts, IEventBus eventBus, IAuditWorkspaces audit, ILogger<TenantAnnouncer> logger)
{
    /// <summary>Records that the user's effective permissions may have changed, and returns the user.</summary>
    public async Task<User> UserPermissionsChangedAsync(int userId, CancellationToken cancellationToken)
    {
        var user = await accounts.GetByIdAsync(userId, cancellationToken);
        await UserPermissionsChangedAsync(user, cancellationToken);
        return user;
    }

    /// <summary>Records that the user's effective permissions may have changed.</summary>
    public Task UserPermissionsChangedAsync(User user, CancellationToken cancellationToken) =>
        eventBus.PublishAsync(new UserPermissionsChangedEvent(user.Id, user.RefId), cancellationToken);

    /// <summary>Records that the role's permissions changed, for everyone who holds it.</summary>
    public Task RolePermissionsChangedAsync(int roleId, CancellationToken cancellationToken) =>
        eventBus.PublishAsync(new RolePermissionsChangedEvent(roleId), cancellationToken);

    /// <summary>Publishes an action on the tenant's people, to be logged in each of its workspaces.</summary>
    /// <remarks>
    /// The action has already happened, so a failure to find the tenant's workspaces is logged rather
    /// than returned: reporting it as failed would only invite a retry of something that succeeded.
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
