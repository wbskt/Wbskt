using System.Net.WebSockets;
using System.Text;

namespace Wbskt.Protocol;

/// <summary>What one read from the socket produced.</summary>
public enum FrameKind
{
    /// <summary>A whole text message, in <see cref="MessageRead.Text"/>.</summary>
    Text,

    /// <summary>A whole binary message, which the protocol does not use.</summary>
    Binary,

    /// <summary>The peer sent its Close frame.</summary>
    Closed,

    /// <summary>The message grew past the size limit before it ended.</summary>
    TooBig
}

public readonly record struct MessageRead(FrameKind Kind, string? Text = null);

/// <summary>
/// Reads whole messages off a WebSocket: a message can span several frames, so they are assembled
/// until EndOfMessage, up to a size limit. One reader per connection, used by its receive loop only.
/// </summary>
public sealed class WebSocketMessageReader : IDisposable
{
    private readonly WebSocket _socket;
    private readonly int _maxMessageBytes;
    private readonly byte[] _buffer = new byte[4 * 1024];
    private readonly MemoryStream _message = new();

    public WebSocketMessageReader(WebSocket socket, int maxMessageBytes)
    {
        _socket = socket;
        _maxMessageBytes = maxMessageBytes;
    }

    public int MaxMessageBytes => _maxMessageBytes;

    public async Task<MessageRead> ReadAsync(CancellationToken cancellationToken)
    {
        _message.SetLength(0);
        WebSocketReceiveResult result;
        do
        {
            result = await _socket.ReceiveAsync(new ArraySegment<byte>(_buffer), cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return new MessageRead(FrameKind.Closed);
            }

            if (_message.Length + result.Count > _maxMessageBytes)
            {
                return new MessageRead(FrameKind.TooBig);
            }

            _message.Write(_buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return result.MessageType == WebSocketMessageType.Text
            ? new MessageRead(FrameKind.Text, Encoding.UTF8.GetString(_message.GetBuffer(), 0, (int)_message.Length))
            : new MessageRead(FrameKind.Binary);
    }

    public void Dispose() => _message.Dispose();
}
