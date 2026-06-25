using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class ChannelRunDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_writes_to_channel()
    {
        // Arrange
        var dispatcher = new ChannelRunDispatcher();
        var request = new BranchExecutionRequest(7, 11, BranchExecutionReason.TriggerStarted);

        // Act
        await dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        var queued = await dispatcher.Reader.ReadAsync(CancellationToken.None);
        Assert.Equal(request, queued);
    }

    [Fact]
    public async Task Reader_returns_requests_in_order()
    {
        // Arrange
        var dispatcher = new ChannelRunDispatcher();
        var first = new BranchExecutionRequest(1, 10, BranchExecutionReason.TriggerStarted);
        var second = new BranchExecutionRequest(1, 11, BranchExecutionReason.ForkChild);
        var third = new BranchExecutionRequest(1, 12, BranchExecutionReason.BookmarkResumed);

        // Act
        await dispatcher.DispatchAsync(first, CancellationToken.None);
        await dispatcher.DispatchAsync(second, CancellationToken.None);
        await dispatcher.DispatchAsync(third, CancellationToken.None);

        // Assert
        Assert.Equal(first, await dispatcher.Reader.ReadAsync(CancellationToken.None));
        Assert.Equal(second, await dispatcher.Reader.ReadAsync(CancellationToken.None));
        Assert.Equal(third, await dispatcher.Reader.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Multiple_concurrent_dispatchers_all_succeed()
    {
        // Arrange
        var dispatcher = new ChannelRunDispatcher();
        var requests = Enumerable.Range(1, 32)
            .Select(index => new BranchExecutionRequest(5, index, BranchExecutionReason.ForkChild))
            .ToArray();

        // Act
        await Task.WhenAll(requests.Select(request => dispatcher.DispatchAsync(request, CancellationToken.None).AsTask()));

        // Assert
        var received = new List<BranchExecutionRequest>();
        for (var index = 0; index < requests.Length; index++)
        {
            received.Add(await dispatcher.Reader.ReadAsync(CancellationToken.None));
        }

        Assert.Equal(requests.OrderBy(request => request.BranchId), received.OrderBy(request => request.BranchId));
    }
}
