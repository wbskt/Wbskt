using Microsoft.Extensions.Options;
using Wbskt.Gateway.Core.Infrastructure;
using Wbskt.Gateway.Core.Telemetry;
using Wbskt.Infrastructure.Hosting;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Gateway.Core.HostedServices;

/// <summary>
/// Closes, as <see cref="DisconnectReason.Revoked"/>, every held connection whose token the cutoffs now
/// refuse. A revoke, delete or secret rotation closes a connection through its event, but the event
/// is lost when it is published while this host's auto-delete queue is gone (a broker restart or a
/// network blip), and the session outlives the blip. The cutoff in Redis is written before the event,
/// so reading it on a timer closes what the event would have, provided a read gets through before the
/// key expires, about an hour after the revoke.
/// </summary>
internal sealed class AccessRecheck(
    IConnectionManager connections,
    IClientAccessGate accessGate,
    GatewayMetrics metrics,
    IOptions<AccessRecheckOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<AccessRecheck> logger)
    : PeriodicWorker(AlwaysOpenWorkGate.Instance, scopeFactory, logger)
{
    // Warned once when the cutoffs become unreadable, not on every tick of an outage.
    private bool _cutoffsUnreadable;

    protected override string Name => "Access recheck";

    protected override TimeSpan Interval { get; } = options.Value.EffectiveInterval;

    // Nothing is held yet when the host starts.
    protected override bool RunOnStart => false;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var held = connections.GetConnections()
            .Where(c => c.Value is { IsOpen: true, CloseRequest: null })
            .ToDictionary(c => c.Key, c => c.Value);
        if (held.Count == 0)
        {
            return;
        }

        var result = await accessGate.RecheckAsync(held.Select(c => new ClientToken(c.Key, c.Value.TokenIssuedAtUtc)).ToList(), ct);
        NoteCutoffsRead(result.CutoffsRead, held.Count);

        // This host's list already refused these: their revoke arrived and is closing them.
        foreach (var clientRefId in result.RefusedHere)
        {
            await CloseAsync(clientRefId, held[clientRefId]);
        }

        foreach (var clientRefId in result.RefusedByCutoffs)
        {
            var connection = held[clientRefId];

            // A revoke whose event is only late lands in this host's list before its close; that is not a miss.
            bool late = accessGate.IsRefusedHere(clientRefId, connection.TokenIssuedAtUtc);
            if (await CloseAsync(clientRefId, connection) && !late)
            {
                Logger.LogWarning("Client {ClientRefId} holds a token its cutoff refuses, and this host never received the revoke; closed.", clientRefId);
                metrics.AccessRecheckClosed();
            }
        }
    }

    private void NoteCutoffsRead(bool read, int held)
    {
        if (!read && !_cutoffsUnreadable)
        {
            Logger.LogWarning("Could not read the token cutoffs; {Count} held connections are checked against this host's list only until they can be read.", held);
        }
        else if (read && _cutoffsUnreadable)
        {
            Logger.LogInformation("The token cutoffs can be read again.");
        }

        _cutoffsUnreadable = !read;
    }

    // True when this check's close was the first asked for: the connection read above, not the client's,
    // so a newer connection with a fresh token is never closed.
    private async Task<bool> CloseAsync(Guid clientRefId, IClientConnection connection)
    {
        try
        {
            return await connection.RequestCloseAsync(DisconnectReason.Revoked, DisconnectReason.Revoked.Describe());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "Closing client {ClientRefId}'s refused connection failed; its session ends on the close request.", clientRefId);
            return true;
        }
    }
}
