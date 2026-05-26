using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class IRunDispatcherTests
{
    [Fact]
    public async Task Stub_dispatcher_records_requests()
    {
        // Arrange
        var dispatcher = new RecordingRunDispatcher();
        var first = new BranchExecutionRequest(42, 1001, BranchExecutionReason.TriggerStarted);
        var second = new BranchExecutionRequest(42, 1002, BranchExecutionReason.ForkChild);

        // Act
        await dispatcher.DispatchAsync(first, CancellationToken.None);
        await dispatcher.DispatchAsync(second, CancellationToken.None);

        // Assert
        Assert.Equal([first, second], dispatcher.Requests);
    }

    private sealed class RecordingRunDispatcher : IRunDispatcher
    {
        public List<BranchExecutionRequest> Requests { get; } = [];

        public ValueTask DispatchAsync(BranchExecutionRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return ValueTask.CompletedTask;
        }
    }
}
