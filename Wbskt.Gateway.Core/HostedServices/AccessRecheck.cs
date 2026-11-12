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
        if (!result.CutoffsRead)
        {
            Logger.LogWarning("Could not read the token cutoffs; {Count} held connections were checked against this host's list only.", held.Count);
        }

        foreach (var clientRefId in result.RefusedHere.Concat(result.RefusedByCutoffs))
        {
            bool missed = result.RefusedByCutoffs.Contains(clientRefId);
            if (missed)
            {
                Logger.LogWarning("Client {ClientRefId} holds a token its cutoff refuses, and this host never received the revoke; closing.", clientRefId);
                metrics.AccessRecheckClosed();
            }

            try
            {
                // The connection read above, not the client's: a newer connection with a fresh token is not this one.
                await held[clientRefId].RequestCloseAsync(DisconnectReason.Revoked, DisconnectReason.Revoked.Describe());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogWarning(ex, "Could not close client {ClientRefId}'s refused connection; the next check tries again.", clientRefId);
            }
        }
    }
}
