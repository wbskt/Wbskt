using System.Net.WebSockets;
using System.Text;

namespace Wbskt.Common.Extensions;

/// <summary>
///     Provides extension methods for WebSocket operations.
/// </summary>
public static class SocketExtensions
{
    private const int BufferSize = 4096; // 4 KB buffer size for reading messages.

    /// <summary>
    ///     Sends a text message asynchronously using the WebSocket.
    /// </summary>
    /// <param name="webSocket">The WebSocket instance.</param>
    /// <param name="message">The message to send.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static Task WriteAsync(this WebSocket webSocket, string message)
    {
        return webSocket.WriteAsync(message, CancellationToken.None);
    }

    /// <summary>
    ///     Sends a text message asynchronously using the WebSocket with a cancellation token.
    /// </summary>
    /// <param name="webSocket">The WebSocket instance.</param>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">The cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task WriteAsync(this WebSocket webSocket, string message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(webSocket);
        ArgumentNullException.ThrowIfNull(message);

        var messageBytes = Encoding.UTF8.GetBytes(message);
        var messageSegment = new ArraySegment<byte>(messageBytes);

        await webSocket.SendAsync(messageSegment, WebSocketMessageType.Text, true, cancellationToken);
    }

    /// <summary>
    ///     Receives a text message asynchronously using the WebSocket.
    /// </summary>
    /// <param name="webSocket">The WebSocket instance.</param>
    /// <returns>A task representing the asynchronous operation, containing the result and message.</returns>
    public static Task<(WebSocketReceiveResult ReceiveResult, string Message)> ReadAsync(this WebSocket webSocket)
    {
        return webSocket.ReadAsync(CancellationToken.None);
    }

    /// <summary>
    ///     Receives a text message asynchronously using the WebSocket with a cancellation token.
    /// </summary>
    /// <param name="webSocket">The WebSocket instance.</param>
    /// <param name="cancellationToken">The cancellation token to observe.</param>
    /// <returns>A task representing the asynchronous operation, containing the result and message.</returns>
    public static async Task<(WebSocketReceiveResult ReceiveResult, string Message)> ReadAsync(this WebSocket webSocket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(webSocket);

        var buffer = new byte[BufferSize];
        var receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

        var message = Encoding.UTF8.GetString(buffer, 0, receiveResult.Count).Trim();
        return (receiveResult, message);
    }
}
