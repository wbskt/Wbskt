using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.HostedServices;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Host.Tests.HostedServices;

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
        Assert.Contains((42L, 1001L, BranchExecutionReason.TriggerStarted), recorder.Calls);
        Assert.Contains((42L, 1002L, BranchExecutionReason.ForkChild), recorder.Calls);
    }

    [Fact]
    public async Task Stop_lets_a_running_step_finish_before_cutting_it_short()
    {
        // Arrange
        var dispatcher = new ChannelRunDispatcher();
        var loop = new SteppingBranchLoop();
        var services = new ServiceCollection();
        services.AddScoped<IBranchLoop>(_ => loop);
        await using var provider = services.BuildServiceProvider();
        var pump = new BranchExecutionPump(dispatcher, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BranchExecutionPump>.Instance, drainGrace: TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await pump.StartAsync(cts.Token);
        await dispatcher.DispatchAsync(new BranchExecutionRequest(42, 1001, BranchExecutionReason.TriggerStarted), cts.Token);
        await loop.StepStarted.Task.WaitAsync(cts.Token);

        // Act
        Task stopping = pump.StopAsync(CancellationToken.None);
        await loop.DrainSeen.Task.WaitAsync(cts.Token);
        Assert.False(stopping.IsCompleted);
        loop.FinishStep.TrySetResult();
        await stopping.WaitAsync(cts.Token);

        // Assert
        Assert.True(loop.StepCompleted);
        Assert.False(loop.StepTokenCancelledDuringStep);
    }

    [Fact]
    public async Task Stop_cuts_a_step_short_once_the_grace_runs_out()
    {
        // Arrange
        var dispatcher = new ChannelRunDispatcher();
        var loop = new SteppingBranchLoop();
        var services = new ServiceCollection();
        services.AddScoped<IBranchLoop>(_ => loop);
        await using var provider = services.BuildServiceProvider();
        var pump = new BranchExecutionPump(dispatcher, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BranchExecutionPump>.Instance, drainGrace: TimeSpan.FromMilliseconds(50));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await pump.StartAsync(cts.Token);
        await dispatcher.DispatchAsync(new BranchExecutionRequest(42, 1001, BranchExecutionReason.TriggerStarted), cts.Token);
        await loop.StepStarted.Task.WaitAsync(cts.Token);

        // Act
        await pump.StopAsync(CancellationToken.None).WaitAsync(cts.Token);

        // Assert
        Assert.False(loop.StepCompleted);
        Assert.True(loop.StepTokenCancelledDuringStep);
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

        public Task RunAsync(long runId, long branchId, BranchExecutionReason reason, CancellationToken drainToken, CancellationToken ct)
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

    // One step that waits to be told to finish, watching both tokens as BranchLoop does.
    private sealed class SteppingBranchLoop : IBranchLoop
    {
        public TaskCompletionSource StepStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DrainSeen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FinishStep { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool StepCompleted { get; private set; }
        public bool StepTokenCancelledDuringStep { get; private set; }

        public async Task RunAsync(long runId, long branchId, BranchExecutionReason reason, CancellationToken drainToken, CancellationToken ct)
        {
            using CancellationTokenRegistration drain = drainToken.Register(() => DrainSeen.TrySetResult());
            StepStarted.TrySetResult();
            try
            {
                await FinishStep.Task.WaitAsync(ct);
                StepCompleted = true;
            }
            catch (OperationCanceledException)
            {
                StepTokenCancelledDuringStep = true;
                throw;
            }
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
