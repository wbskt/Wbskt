using MassTransit;
using Wbskt.Events.Hosting;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Management.Host.Handlers;

/// <summary>Records each socket host's heartbeat; <see cref="Services.StalePresenceSweeper"/> reads it.</summary>
public sealed class SocketHostHeartbeatHandler : IConsumer<SocketHostHeartbeat>
{
    private readonly ISocketHostProvider _socketHostProvider;

    public SocketHostHeartbeatHandler(ISocketHostProvider socketHostProvider)
    {
        _socketHostProvider = socketHostProvider;
    }

    public Task Consume(ConsumeContext<SocketHostHeartbeat> context)
    {
        return _socketHostProvider.RecordHeartbeatAsync(context.Message.HostId, context.CancellationToken);
    }
}
