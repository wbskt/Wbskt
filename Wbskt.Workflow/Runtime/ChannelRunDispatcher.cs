using System.Collections.Concurrent;
using System.Threading.Channels;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class ChannelRunDispatcher : IRunDispatcher, IRunDispatchRecorder
{
    private readonly Channel<BranchExecutionRequest> _channel;
    private ConcurrentDictionary<long, byte>? _recorded;

    public ChannelRunDispatcher()
    {
        _channel = Channel.CreateUnbounded<BranchExecutionRequest>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
    }

    public ChannelReader<BranchExecutionRequest> Reader => _channel.Reader;

    public int Count
    {
        get
        {
            try
            {
                return Reader.Count;
            }
            catch (NotSupportedException)
            {
                return 0;
            }
        }
    }

    public ValueTask DispatchAsync(BranchExecutionRequest request, CancellationToken ct)
    {
        Volatile.Read(ref _recorded)?.TryAdd(request.BranchId, 0);
        return _channel.Writer.WriteAsync(request, ct);
    }

    public void StartRecording()
    {
        Volatile.Write(ref _recorded, new ConcurrentDictionary<long, byte>());
    }

    public IReadOnlySet<long> StopRecording()
    {
        ConcurrentDictionary<long, byte>? recorded = Interlocked.Exchange(ref _recorded, null);
        return recorded is null ? new HashSet<long>() : new HashSet<long>(recorded.Keys);
    }
}
