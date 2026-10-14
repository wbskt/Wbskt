using Wbskt.Infrastructure.Hosting;
using Wbskt.Infrastructure.Telemetry;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Telemetry;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

/// <summary>
/// A <see cref="PeriodicWorker"/> that only the active engine runs: a standby skips every tick.
/// Its ticks are counted on <see cref="WorkflowMetrics"/>.
/// </summary>
public abstract class LeaderPeriodicWorker(ILeadershipGate leadership, IServiceScopeFactory scopeFactory, ILogger logger)
    : PeriodicWorker(new LeadershipWorkGate(leadership), scopeFactory, logger)
{
    protected override IWorkerTickMetrics? FindTickMetrics(IServiceProvider services) => services.GetService<WorkflowMetrics>();

    private sealed class LeadershipWorkGate(ILeadershipGate leadership) : IWorkGate
    {
        public bool IsOpen => leadership.IsActive;
    }
}
