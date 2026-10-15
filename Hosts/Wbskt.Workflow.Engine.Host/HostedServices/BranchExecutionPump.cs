using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

internal sealed class BranchExecutionPump : BackgroundService
{
    private readonly ChannelRunDispatcher _dispatcher;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BranchExecutionPump> _logger;
    private readonly int _branchWorkerLimit;

    [ActivatorUtilitiesConstructor]
    public BranchExecutionPump(
        ChannelRunDispatcher dispatcher,
        IServiceScopeFactory scopeFactory,
        ILogger<BranchExecutionPump> logger,
        IOptions<WorkflowEngineOptions> options)
        : this(dispatcher, scopeFactory, logger, options.Value.BranchWorkerLimit)
    {
    }

    internal BranchExecutionPump(
        ChannelRunDispatcher dispatcher,
        IServiceScopeFactory scopeFactory,
        ILogger<BranchExecutionPump> logger,
        int? branchWorkerLimit = null)
    {
        _dispatcher = dispatcher;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _branchWorkerLimit = branchWorkerLimit ?? new WorkflowEngineOptions().BranchWorkerLimit;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using SemaphoreSlim concurrencyLimiter = new(_branchWorkerLimit, _branchWorkerLimit);

        while (await _dispatcher.Reader.WaitToReadAsync(stoppingToken))
        {
            while (_dispatcher.Reader.TryRead(out BranchExecutionRequest? request))
            {
                await concurrencyLimiter.WaitAsync(stoppingToken);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                        IBranchLoop loop = scope.ServiceProvider.GetRequiredService<IBranchLoop>();
                        await loop.RunAsync(request.RunId, request.BranchId, request.Reason, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Branch execution failed for RunId {RunId} BranchId {BranchId}.", request.RunId, request.BranchId);
                        await ContainLeakedBranchAsync(request, stoppingToken);
                    }
                    finally
                    {
                        concurrencyLimiter.Release();
                    }
                }, stoppingToken);
            }
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

            await branchProvider.SetFailedAsync(request.BranchId, errorJson, ct);

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
