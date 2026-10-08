using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Services;

/// <summary>
/// The one place a device gains or loses access: it writes the token cutoff and then announces the
/// change, in that order, so a revoked device is refused before anyone hears it was revoked. The
/// cutoff in Redis is what holds when a socket host restarts or misses the event; the event is the
/// fast path that also closes the live connection. See <see cref="ClientTokenCutoffs"/>.
/// </summary>
/// <remarks>
/// Built by its owner around the bus that owner publishes on (the queued bus for console changes,
/// the consumer's bus for workspace retirement) rather than resolved from the container, so the
/// choice of bus stays where it is made today.
/// </remarks>
internal sealed class ClientAccessRevoker
{
    private readonly IEventBus _eventBus;
    private readonly IClientTokenCutoffs _cutoffs;
    private readonly TimeProvider _time;

    public ClientAccessRevoker(IEventBus eventBus, IClientTokenCutoffs cutoffs, TimeProvider? time = null)
    {
        _eventBus = eventBus;
        _cutoffs = cutoffs;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// A client's status changed. Becoming Registered lets it sign in afresh; leaving Registered
    /// refuses every token it holds; moving between two statuses without access changes nothing.
    /// </summary>
    public async Task StatusChangedAsync(int workspaceId, ClientStatusChange change, ClientStatus status, CancellationToken cancellationToken)
    {
        if (status == ClientStatus.Registered)
        {
            await _cutoffs.ReinstateAsync(change.RefId, _time.GetUtcNow().UtcDateTime);
        }
        else if (change.OldStatus == ClientStatus.Registered)
        {
            await _cutoffs.RevokeAsync(change.RefId);
        }

        await _eventBus.PublishAsync(
            new ClientStatusChangedEvent(change.RefId, change.Id, change.PolicyRefId, change.PolicyId, workspaceId, (byte)status),
            cancellationToken);
    }

    /// <summary>A client was revoked by something other than a status change on it, such as its workspace being deleted.</summary>
    public async Task RevokedAsync(int workspaceId, Guid clientRefId, int clientId, Guid policyRefId, int policyId, CancellationToken cancellationToken)
    {
        await _cutoffs.RevokeAsync(clientRefId);
        await _eventBus.PublishAsync(
            new ClientStatusChangedEvent(clientRefId, clientId, policyRefId, policyId, workspaceId, (byte)ClientStatus.Revoked),
            cancellationToken);
    }

    /// <summary>A client was deleted: every token it holds is refused.</summary>
    public async Task DeletedAsync(Client client, CancellationToken cancellationToken)
    {
        await _cutoffs.RevokeAsync(client.RefId);
        await _eventBus.PublishAsync(
            new ClientDeletedEvent(client.RefId, client.Id, client.PolicyRefId, client.PolicyId, client.WorkspaceId, client.Name),
            cancellationToken);
    }

    /// <summary>
    /// A client's secret was replaced: tokens issued before now are refused. The cutoff and the event
    /// carry the same moment, so Redis and the socket hosts' own lists agree.
    /// </summary>
    public async Task SecretRotatedAsync(Client client, CancellationToken cancellationToken)
    {
        var rotatedAt = _time.GetUtcNow().UtcDateTime;
        await _cutoffs.RevokeIssuedBeforeAsync(client.RefId, rotatedAt);
        await _eventBus.PublishAsync(new ClientSecretRotatedEvent(client.RefId, client.Id, client.WorkspaceId, rotatedAt), cancellationToken);
    }
}
