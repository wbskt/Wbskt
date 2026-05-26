using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.HostedServices;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.HostedServices;

public sealed class BranchExecutionPumpTests
{
    [Fact]
    public async Task Pump_drains_channel_and_invokes_branch_loop()
    {
        // Arrange
        var dispatcher = new ChannelRunDispatcher();
        var recorder = new BranchLoopRecorder();
        await using var provider = CreateProvider(recorder, shouldThrow: false);
        var pump = new BranchExecutionPump(dispatcher, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BranchExecutionPump>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await pump.StartAsync(cts.Token);
        await dispatcher.DispatchAsync(new BranchExecutionRequest(42, 1001, BranchExecutionReason.TriggerStarted), cts.Token);
        await recorder.WaitForCountAsync(1, cts.Token);
        await pump.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal([(42L, 1001L, BranchExecutionReason.TriggerStarted)], recorder.Calls);
    }

    [Fact]
    public async Task Pump_continues_after_loop_throws()
    {
        // Arrange
        var dispatcher = new ChannelRunDispatcher();
        var recorder = new BranchLoopRecorder();
        await using var provider = CreateProvider(recorder, shouldThrow: true);
        var pump = new BranchExecutionPump(dispatcher, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BranchExecutionPump>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await pump.StartAsync(cts.Token);
        await dispatcher.DispatchAsync(new BranchExecutionRequest(42, 1001, BranchExecutionReason.TriggerStarted), cts.Token);
        await dispatcher.DispatchAsync(new BranchExecutionRequest(42, 1002, BranchExecutionReason.ForkChild), cts.Token);
        await recorder.WaitForCountAsync(2, cts.Token);
        await pump.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, recorder.Calls.Count);
        Assert.Equal((42L, 1002L, BranchExecutionReason.ForkChild), recorder.Calls[1]);
    }

    private static ServiceProvider CreateProvider(BranchLoopRecorder recorder, bool shouldThrow)
    {
        var services = new ServiceCollection();
        services.AddScoped<IBranchLoop>(_ => new RecordingBranchLoop(recorder, shouldThrow));
        return services.BuildServiceProvider();
    }

    private sealed class RecordingBranchLoop(BranchLoopRecorder recorder, bool shouldThrow) : IBranchLoop
    {
        private bool _hasThrown;

        public Task RunAsync(long runId, long branchId, BranchExecutionReason reason, CancellationToken ct)
        {
            recorder.Record((runId, branchId, reason));
            if (shouldThrow && !_hasThrown)
            {
                _hasThrown = true;
                throw new InvalidOperationException("boom");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class BranchLoopRecorder
    {
        private readonly object _sync = new();
        private TaskCompletionSource _changed = NewSignal();

        public List<(long RunId, long BranchId, BranchExecutionReason Reason)> Calls { get; } = [];

        public void Record((long RunId, long BranchId, BranchExecutionReason Reason) call)
        {
            lock (_sync)
            {
                Calls.Add(call);
                _changed.TrySetResult();
                _changed = NewSignal();
            }
        }

        public async Task WaitForCountAsync(int expectedCount, CancellationToken ct)
        {
            while (true)
            {
                Task signal;
                lock (_sync)
                {
                    if (Calls.Count >= expectedCount)
                    {
                        return;
                    }

                    signal = _changed.Task;
                }

                await signal.WaitAsync(ct);
            }
        }

        private static TaskCompletionSource NewSignal()
        {
            return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
