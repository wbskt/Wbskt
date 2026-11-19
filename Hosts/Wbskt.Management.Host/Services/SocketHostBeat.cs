using Microsoft.Extensions.Options;
using Wbskt.Infrastructure.Clients;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Management.Host.Services;

/// <summary>
/// Records a socket host's beat, from its heartbeat or its presence snapshot. A host that had been silent
/// past <see cref="CommandQueueOptions.HostSilentAfterSeconds"/> had the commands for its clients queued
/// rather than addressed to it (#960); its clients are still connected and will not connect again to take
/// them, so the beat that shows it alive sends them on.
/// </summary>
public sealed class SocketHostBeat(
    ISocketHostProvider hosts,
    ClientCommandQueue commandQueue,
    IOptions<CommandQueueOptions> queueOptions,
    ILogger<SocketHostBeat> logger)
{
    public async Task RecordAsync(string hostId, CancellationToken ct)
    {
        var waiting = await hosts.RecordHeartbeatAsync(hostId, queueOptions.Value.HostSilentAfterSeconds, ct);
        foreach (var client in waiting)
        {
            try
            {
                await commandQueue.DeliverAsync(client.ClientRefId, client.ClientId, client.WorkspaceId, hostId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // DeliverAsync put the unsent ones back; the client's next connect, or the next silence, sends them.
                logger.LogWarning(ex, "Socket host {HostId} is heard again, but client {ClientRefId}'s queued commands could not be sent.", hostId, client.ClientRefId);
            }
        }

        if (waiting.Count > 0)
        {
            logger.LogInformation("Socket host {HostId} is heard again after going silent; sent the queued commands of {Count} client(s) it holds.", hostId, waiting.Count);
        }
    }
}
