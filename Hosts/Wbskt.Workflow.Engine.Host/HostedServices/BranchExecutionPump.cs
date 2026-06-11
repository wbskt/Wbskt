using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

internal sealed class BranchExecutionPump : BackgroundService
{
    private readonly ChannelRunDispatcher _dispatcher;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BranchExecutionPump> _logger;

    public BranchExecutionPump(ChannelRunDispatcher dispatcher, IServiceScopeFactory scopeFactory, ILogger<BranchExecutionPump> logger)
    {
        _dispatcher = dispatcher;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await _dispatcher.Reader.WaitToReadAsync(stoppingToken))
        {
            while (_dispatcher.Reader.TryRead(out BranchExecutionRequest? request))
            {
                try
                {
                    await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                    IBranchLoop loop = scope.ServiceProvider.GetRequiredService<IBranchLoop>();
                    await loop.RunAsync(request.RunId, request.BranchId, request.Reason, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Branch execution failed for RunId {RunId} BranchId {BranchId}.", request.RunId, request.BranchId);
                }
            }
        }
    }
}
