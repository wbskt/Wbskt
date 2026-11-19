using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

/// <summary>
/// Runs each dispatched branch on a worker, up to <see cref="WorkflowEngineOptions.BranchWorkerLimit"/> at
/// once. Once the host starts stopping (<see cref="IHostApplicationLifetime.ApplicationStopping"/>, before
/// any hosted service is stopped) it reads no further request and starts no new node. On stop it drains: every worker finishes the step it is in (the
/// loop starts no new one) for up to <see cref="WorkflowEngineOptions.BranchDrainGrace"/>, and only then is
/// the step token cancelled. Cutting steps short on the host token made the next leader run each again,
/// so an email, webhook or client command already sent went twice. The bus must still be up while it
/// drains, so a step can publish its command or notification: the pump is registered after
/// <see cref="EngineLeadershipCoordinator"/> and therefore stopped before it. The drain never runs past
/// the moment the lease's gate closes (<see cref="LeadershipState.WorkStopsAtUtc"/>): a process stopping
/// because it could not renew must not finish a step the next leader is already running again.
/// </summary>
internal sealed class BranchExecutionPump : BackgroundService
{
    private readonly ChannelRunDispatcher _dispatcher;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BranchExecutionPump> _logger;
    private readonly CancellationToken _hostStopping;
    private readonly Func<TimeSpan?> _leaseLeft;
    private readonly TimeProvider _clock;
    private CancellationTokenSource? _draining;
    private readonly TimeSpan _drainGrace;
    private readonly TimeSpan _leaseRecheck = TimeSpan.FromSeconds(1);
    private readonly SemaphoreSlim _concurrencyLimiter;
    private readonly CancellationTokenSource _stepCts = new();
    private readonly ConcurrentDictionary<Task, byte> _workers = new();

    [ActivatorUtilitiesConstructor]
    public BranchExecutionPump(
        ChannelRunDispatcher dispatcher,
        IServiceScopeFactory scopeFactory,
        ILogger<BranchExecutionPump> logger,
        IOptions<WorkflowEngineOptions> options,
        IHostApplicationLifetime lifetime,
        LeadershipState leadership,
        TimeProvider clock)
        : this(
            dispatcher,
            scopeFactory,
            logger,
            options.Value.BranchWorkerLimit,
            options.Value.BranchDrainGrace,
            lifetime.ApplicationStopping,
            () => leadership.IsLeader ? leadership.WorkStopsAtUtc - clock.GetUtcNow().UtcDateTime : null,
            clock)
    {
    }

    internal BranchExecutionPump(
        ChannelRunDispatcher dispatcher,
        IServiceScopeFactory scopeFactory,
        ILogger<BranchExecutionPump> logger,
        int? branchWorkerLimit = null,
        TimeSpan? drainGrace = null,
        CancellationToken hostStopping = default,
        Func<TimeSpan?>? leaseLeft = null,
        TimeProvider? clock = null)
    {
        _dispatcher = dispatcher;
        _clock = clock ?? TimeProvider.System;
        _hostStopping = hostStopping;
        _leaseLeft = leaseLeft ?? (() => null);
        _scopeFactory = scopeFactory;
        _logger = logger;
        int limit = branchWorkerLimit ?? new WorkflowEngineOptions().BranchWorkerLimit;
        _concurrencyLimiter = new SemaphoreSlim(limit, limit);
        _drainGrace = drainGrace ?? new WorkflowEngineOptions().BranchDrainGrace;
    }

    protected override async Task ExecuteAsync(CancellationToken executeToken)
    {
        // The drain starts when the host starts stopping, not when this service is stopped: the services
        // stopped before it would otherwise leave it starting new nodes meanwhile.
        // Disposed with the pump, not here: workers still running hold its token.
        _draining = CancellationTokenSource.CreateLinkedTokenSource(executeToken, _hostStopping);
        CancellationToken stoppingToken = _draining.Token;
        try
        {
            await PumpAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task PumpAsync(CancellationToken stoppingToken)
    {
        while (await _dispatcher.Reader.WaitToReadAsync(stoppingToken))
        {
            while (!stoppingToken.IsCancellationRequested && _dispatcher.Reader.TryRead(out BranchExecutionRequest? request))
            {
                await _concurrencyLimiter.WaitAsync(stoppingToken);

                Task worker = Task.Run(() => RunBranchAsync(request, stoppingToken), CancellationToken.None);
                _workers.TryAdd(worker, 0);
                _ = worker.ContinueWith(finished => _workers.TryRemove(finished, out _), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Cancels the host token (already cancelled when the host is stopping), so the reader stops and
        // each loop starts no further node.
        await base.StopAsync(cancellationToken);

        Task drained = Task.WhenAll(_workers.Keys);
        if (!drained.IsCompleted)
        {
            _logger.LogInformation("Waiting up to {Grace} for {Count} running branch(es) to finish their step.", _drainGrace, _workers.Count);
            try
            {
                if (!await DrainAsync(drained, cancellationToken))
                {
                    _logger.LogWarning("{Count} branch(es) still running after the drain grace or the lease ran out; cutting their step short. The next leader runs it again.", _workers.Count);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Host shutdown timeout reached while draining {Count} branch(es); cutting their step short.", _workers.Count);
            }
        }

        await _stepCts.CancelAsync();
        try
        {
            await Task.WhenAll(_workers.Keys).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("{Count} branch(es) did not unwind before the host shutdown timeout.", _workers.Count);
        }
    }

    /// <summary>
    /// Waits for the running steps up to the configured grace, but never past the moment the lease's gate
    /// closes. The lease is read again every slice, because it is renewed while the pump drains: read once
    /// at the start, a renewal running a little late would cut a normal deploy's drain short. False when
    /// the steps were still running at the end.
    /// </summary>
    private async Task<bool> DrainAsync(Task drained, CancellationToken cancellationToken)
    {
        long started = _clock.GetTimestamp();
        while (!drained.IsCompleted)
        {
            TimeSpan slice = _drainGrace - _clock.GetElapsedTime(started);
            if (_leaseLeft() is { } left && left < slice)
            {
                slice = left;
            }

            if (slice <= TimeSpan.Zero)
            {
                return false;
            }

            try
            {
                await drained.WaitAsync(slice < _leaseRecheck ? slice : _leaseRecheck, _clock, cancellationToken);
            }
            catch (TimeoutException)
            {
                // The next slice reads the lease again.
            }
        }

        return true;
    }

    public override void Dispose()
    {
        base.Dispose();

        // Only once nothing can release it any more; a worker still unwinding past the shutdown
        // timeout is abandoned with the process.
        if (_workers.IsEmpty)
        {
            _concurrencyLimiter.Dispose();
        }

        _stepCts.Dispose();
        _draining?.Dispose();
    }

    private async Task RunBranchAsync(BranchExecutionRequest request, CancellationToken drainToken)
    {
        CancellationToken stepToken = _stepCts.Token;
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            IBranchLoop loop = scope.ServiceProvider.GetRequiredService<IBranchLoop>();
            await loop.RunAsync(request.RunId, request.BranchId, request.Reason, drainToken, stepToken);
        }
        catch (OperationCanceledException) when (stepToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Branch execution failed for RunId {RunId} BranchId {BranchId}.", request.RunId, request.BranchId);
            await ContainLeakedBranchAsync(request, stepToken);
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    /// <summary>
    /// Last line of defence for an exception that escaped <see cref="IBranchLoop.RunAsync"/>.
    /// Without this the branch stays Active forever: the finalizer never runs because
    /// ActiveBranchCount is never decremented, Run_GetStuck deliberately skips runs that still have
    /// Active branches so the reaper won't collect it, and RunRecoveryService re-dispatches it into
    /// the same failure on every restart.
    ///
    /// Idempotent by design - it only acts when the branch is still Active, so a branch that ended
    /// itself before the exception escaped is left alone and its slot is not released twice.
    ///
    /// Every step is individually guarded: if remediation itself fails the run is left to the
    /// RunReaper rather than taking down the pump's worker.
    /// </summary>
    private async Task ContainLeakedBranchAsync(BranchExecutionRequest request, CancellationToken ct)
    {
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            var branchProvider = scope.ServiceProvider.GetRequiredService<IBranchProvider>();

            BranchRow branch = await branchProvider.GetByIdAsync(request.BranchId, ct);
            if (!string.Equals(branch.Status, BranchStatus.Active, StringComparison.Ordinal))
            {
                _logger.LogDebug(
                    "Branch {BranchId} is already {Status}; no containment needed.",
                    request.BranchId,
                    branch.Status);
                return;
            }

            _logger.LogWarning(
                "Containing leaked branch {BranchId} on run {RunId}: marking Failed so the run can finalize.",
                request.BranchId,
                request.RunId);

            // A fixed message: the branch's error is shown to the workspace, and an exception's own
            // message can name tables, hosts or connection details. The exception is in the log above.
            string errorJson = System.Text.Json.JsonSerializer.Serialize(
                new { ErrorCode = "BRANCH_LOOP_ESCAPED", Message = "The engine hit an unexpected error running this branch." },
                EngineJson.Options);

            // The status read above can be stale by now; the end itself is the compare-and-set that
            // decides whether this path or the branch's own loop releases it.
            if (!await branchProvider.EndIfLiveAsync(request.BranchId, BranchStatus.Failed, errorJson, ct))
            {
                _logger.LogWarning(
                    "Branch {BranchId} on run {RunId} ended before it could be contained; not releasing it again.",
                    request.BranchId,
                    request.RunId);
                return;
            }

            var branchTerminator = scope.ServiceProvider.GetRequiredService<IBranchTerminator>();
            await branchTerminator.ReleaseAsync(checked((int)request.RunId), 1, ct);
        }
        catch (Exception containmentFailure)
        {
            // Nothing further to try - the RunReaper is the backstop for this run.
            _logger.LogError(
                containmentFailure,
                "Failed to contain leaked branch {BranchId} on run {RunId}; leaving it to the RunReaper.",
                request.BranchId,
                request.RunId);
        }
    }
}
