using Wbskt.Client.Sdk.Models;
using Wbskt.Socket.Host.Infrastructure;

namespace Wbskt.Socket.Host.Services.Inbound;

/// <summary>The device a message came from.</summary>
public sealed record InboundContext(Guid ClientRefId, ClientConnection Connection);

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
