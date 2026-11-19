using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Models.Admin;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// Publishes what tenant administration changed, once it has committed: that someone's access may have
/// changed, and an action on the tenant's people for the audit log of every workspace in the tenant. It
/// is given the queued bus (<c>AddScopedWithQueuedEvents</c>), so a broker that is down delays these
/// events instead of failing a request whose change has already happened.
/// </summary>
/// <remarks>
/// An access change is announced on <see cref="WorkspaceAccessChanges"/>, which is what empties the
/// management host's cached workspace access and rechecks open live feeds. Announcing it here, rather
/// than only from a filter on the public controllers, covers every path that changes access: staff
/// suspensions and removals, account deletion, and whatever calls this next (#949).
/// </remarks>
internal sealed class TenantAnnouncer(IAccountProvider accounts, IEventBus eventBus, IAuditWorkspaces audit, WorkspaceAccessChanges accessChanges, ILogger<TenantAnnouncer> logger)
{
    /// <summary>
    /// Announces that someone's access to some workspace may have changed: a role, group, grant,
    /// membership or suspension. Call it once the change has committed.
    /// </summary>
    public void AccessChanged() => accessChanges.Publish();

    /// <summary>Announces an access change of the user's, and returns the user for the audit event that follows.</summary>
    public async Task<User> AccessChangedAsync(int userId, CancellationToken cancellationToken)
    {
        AccessChanged();
        return await accounts.GetByIdAsync(userId, cancellationToken);
    }

    /// <summary>Publishes an action on the tenant's people, to be logged in each of its workspaces.</summary>
    /// <remarks>
    /// The action has already happened, so a failure to find the tenant's workspaces is logged rather
    /// than returned: reporting it as failed would only invite a retry of something that succeeded.
    /// </remarks>
    public Task ToTenantAsync(TenantActorEvent @event, int tenantId, CancellationToken cancellationToken) =>
        PublishAsync(@event, () => audit.OfTenantAsync(tenantId, cancellationToken), cancellationToken);

    /// <summary>Publishes an action on one person, to be logged in each workspace they are a member of.</summary>
    public Task ToUserAsync(TenantActorEvent @event, int userId, CancellationToken cancellationToken) =>
        PublishAsync(@event, () => audit.OfUserAsync(userId, cancellationToken), cancellationToken);

    /// <summary>
    /// Tells the customer's audit log that Wbskt support made a change, once the staff runner has committed
    /// it, and not for a replay, which was announced the first time (<see cref="SupportActionTakenEvent"/>).
    /// </summary>
    public async Task SupportActionAsync(Result<StaffActionResponse> result, SupportActionTakenEvent @event, Func<CancellationToken, Task<int?>> tenantId)
    {
        if (result.IsFailure || result.Value.Replayed)
        {
            return;
        }

        // The change has committed, so nothing here may fail the request or take the caller's token.
        int? id;
        try
        {
            id = await tenantId(CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not find the tenant of support action {Action} on {TargetRefId} for the audit log.", @event.Action, @event.TargetRefId);
            return;
        }

        if (id is { } found)
        {
            await ToTenantAsync(@event, found, CancellationToken.None);
        }
    }

    private async Task PublishAsync(TenantActorEvent @event, Func<Task<IReadOnlyList<int>>> workspaces, CancellationToken cancellationToken)
    {
        try
        {
            await eventBus.PublishAsync(@event with { WorkspaceIds = await workspaces() }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not publish {EventType} for the audit log.", @event.GetType().Name);
        }
    }
}
