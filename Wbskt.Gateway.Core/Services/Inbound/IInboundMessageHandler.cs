using Wbskt.Protocol;
using Wbskt.Gateway.Core.Infrastructure;

namespace Wbskt.Gateway.Core.Services.Inbound;

/// <summary>The device a message came from.</summary>
public sealed record InboundContext(Guid ClientRefId, IDeviceConnection Connection);

/// <summary>
/// Handles one protocol message type from devices. Adding a message to the protocol is a new
/// implementation registered in DI; <see cref="InboundMessageDispatcher"/> routes by <see cref="Type"/>.
/// </summary>
public interface IInboundMessageHandler
{
    /// <summary>The message type handled, such as <c>sys.pong</c>.</summary>
    string Type { get; }

    Task HandleAsync(InboundContext context, SocketMessage message, CancellationToken cancellationToken);
}
