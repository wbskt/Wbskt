using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Services;

public sealed class EventLogBuffer
{
    private readonly Channel<EventLogEntry> _channel;

    public EventLogBuffer(IOptions<EventLoggingOptions> options)
    {
        var maxBufferSize = options.Value.MaxBufferSize;
        
        var channelOptions = new BoundedChannelOptions(maxBufferSize)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        _channel = Channel.CreateBounded<EventLogEntry>(channelOptions);
    }

    public ValueTask WriteAsync(EventLogEntry entry, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(entry, cancellationToken);
    }

    public IAsyncEnumerable<EventLogEntry> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
