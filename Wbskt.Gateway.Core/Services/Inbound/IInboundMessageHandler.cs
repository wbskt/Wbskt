using Wbskt.Protocol;
using Wbskt.Gateway.Core.Infrastructure;

namespace Wbskt.Gateway.Core.Services.Inbound;

/// <summary>The client a message came from.</summary>
public sealed record InboundContext(Guid ClientRefId, IClientConnection Connection)
{
    /// <summary>The session's message allowance, for its own messages (not <c>sys.*</c>); none lets every one through.</summary>
    public ClientAllowance? Allowance { get; init; }
}

/// <summary>
/// Handles one protocol message type from clients. Adding a message to the protocol is a new
/// implementation registered in DI; <see cref="InboundMessageDispatcher"/> routes by <see cref="Type"/>.
/// </summary>
public interface IInboundMessageHandler
{
    /// <summary>The message type handled, such as <c>sys.pong</c>.</summary>
    string Type { get; }

    Task HandleAsync(InboundContext context, ProtocolMessage message, CancellationToken cancellationToken);
}
