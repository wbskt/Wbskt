namespace Wbskt.Gateway.MQTT.Host.Infrastructure;

/// <summary>
/// Reads an MQTT connection's bytes as they arrive and fails the read that brings in the header of a
/// packet longer than <see cref="MaxPacketBytes"/>, before anything buffers its body.
/// </summary>
/// <remarks>
/// MQTTnet's server has no maximum packet size: it allocates whatever a packet's remaining length
/// claims, up to 256 MB, and does so for CONNECT before the client has authenticated. This stream
/// sits under it and follows each packet's fixed header (one type byte, then the remaining length in
/// one to four bytes of 7 bits each) and skips the body, so it adds a few comparisons per byte and
/// holds nothing.
/// </remarks>
public sealed class PacketSizeGuardStream : Stream
{
    private readonly Stream _inner;

    private State _state = State.Type;
    private int _lengthBytes;
    private int _remainingLength;
    private int _bodyLeft;

    public PacketSizeGuardStream(Stream inner, int maxPacketBytes)
    {
        _inner = inner;
        MaxPacketBytes = maxPacketBytes;
    }

    private enum State
    {
        Type,
        Length,
        Body
    }

    /// <summary>The largest remaining length (everything after the fixed header) a packet may claim.</summary>
    public int MaxPacketBytes { get; }

    /// <summary>Set once a packet over the limit was refused; the connection then ends.</summary>
    public bool Exceeded { get; private set; }

    public override bool CanRead => true;

    public override bool CanWrite => true;

    public override bool CanSeek => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken);
        Inspect(buffer.Span[..read]);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        Inspect(buffer.AsSpan(offset, read));
        return read;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        _inner.WriteAsync(buffer, cancellationToken);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _inner.WriteAsync(buffer, offset, count, cancellationToken);

    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    public override void Flush() => _inner.Flush();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync() => _inner.DisposeAsync();

    private void Inspect(ReadOnlySpan<byte> bytes)
    {
        var i = 0;
        while (i < bytes.Length)
        {
            switch (_state)
            {
                case State.Type:
                    i++;
                    _remainingLength = 0;
                    _lengthBytes = 0;
                    _state = State.Length;
                    break;
                case State.Length:
                    var next = bytes[i++];
                    _remainingLength |= (next & 0x7F) << (7 * _lengthBytes);
                    _lengthBytes++;
                    if ((next & 0x80) != 0)
                    {
                        if (_lengthBytes == 4)
                        {
                            throw new IOException("Malformed MQTT packet: the remaining length runs past four bytes.");
                        }

                        break;
                    }

                    if (_remainingLength > MaxPacketBytes)
                    {
                        Exceeded = true;
                        throw new IOException($"MQTT packet of {_remainingLength} bytes is over the {MaxPacketBytes}-byte limit.");
                    }

                    _bodyLeft = _remainingLength;
                    _state = _bodyLeft == 0 ? State.Type : State.Body;
                    break;
                default:
                    var skip = Math.Min(_bodyLeft, bytes.Length - i);
                    i += skip;
                    _bodyLeft -= skip;
                    if (_bodyLeft == 0)
                    {
                        _state = State.Type;
                    }

                    break;
            }
        }
    }
}
