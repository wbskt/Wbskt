using System.Threading.Channels;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

public sealed class ChannelRunDispatcher : IRunDispatcher
{
    private readonly Channel<BranchExecutionRequest> _channel;

    public ChannelRunDispatcher()
    {
        _channel = Channel.CreateUnbounded<BranchExecutionRequest>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
    }

    public ChannelReader<BranchExecutionRequest> Reader => _channel.Reader;

    public ValueTask DispatchAsync(BranchExecutionRequest request, CancellationToken ct)
    {
        return _channel.Writer.WriteAsync(request, ct);
    }
}
