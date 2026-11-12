using Microsoft.Extensions.Options;
using Wbskt.Gateway.Core.Infrastructure;
using Wbskt.Infrastructure.Hosting;

namespace Wbskt.Gateway.Core.HostedServices;

/// <summary>
/// Closes, as <see cref="DisconnectReason.Revoked"/>, every held connection whose token the cutoffs now
/// refuse. A revoke, delete or secret rotation closes a connection through its event, but the event
/// is lost when it is published while this host's auto-delete queue is gone (a broker restart or a
/// network blip), and the session outlives the blip. The cutoff in Redis is written before the event,
/// so asking it on a timer closes what the event would have.
/// </summary>
internal sealed class AccessRecheck(
    IConnectionManager connections,
    IClientAccessGate accessGate,
    IOptions<AccessRecheckOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<AccessRecheck> logger)
    : PeriodicWorker(AlwaysOpenWorkGate.Instance, scopeFactory, logger)
{
    protected override string Name => "Access recheck";

    protected override TimeSpan Interval { get; } = options.Value.Interval > TimeSpan.Zero ? options.Value.Interval : AccessRecheckOptions.DefaultInterval;

    // Every connection held at start was admitted against the cutoffs moments ago.
    protected override bool RunOnStart => false;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        foreach (var (clientRefId, connection) in connections.GetConnections())
        {
            if (!connection.IsOpen || connection.CloseRequest != null)
            {
                continue;
            }

            if (await accessGate.IsRefusedAsync(clientRefId, connection.TokenIssuedAtUtc, ct))
            {
                Logger.LogWarning("Client {ClientRefId} holds a token its cutoff refuses; closing a connection whose revoke was missed.", clientRefId);
                await connection.RequestCloseAsync(DisconnectReason.Revoked, "Client access revoked.");
            }
        }
    }
}
