using MassTransit;
using Wbskt.Events.Hosting;
using Wbskt.Management.Host.Services;

namespace Wbskt.Management.Host.Handlers;

/// <summary>
/// Records each socket host's heartbeat; <see cref="StalePresenceSweeper"/> reads it. A beat ending a
/// silence also sends the commands that queued for the host's clients meanwhile (<see cref="SocketHostBeat"/>).
/// </summary>
public sealed class SocketHostHeartbeatHandler : IConsumer<SocketHostHeartbeat>
{
    private readonly SocketHostBeat _beat;

    public SocketHostHeartbeatHandler(SocketHostBeat beat)
    {
        _beat = beat;
    }

    public Task Consume(ConsumeContext<SocketHostHeartbeat> context)
    {
        return _beat.RecordAsync(context.Message.HostId, context.CancellationToken);
    }
}
