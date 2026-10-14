using Wbskt.Infrastructure.Hosting;
using Wbskt.Workflow.Abstraction.Engine;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

/// <summary>
/// A <see cref="PeriodicWorker"/> that only the active engine runs: a standby skips every tick.
/// </summary>
public abstract class LeaderPeriodicWorker(ILeadershipGate leadership, IServiceScopeFactory scopeFactory, ILogger logger)
    : PeriodicWorker(new LeadershipWorkGate(leadership), scopeFactory, logger)
{
    private sealed class LeadershipWorkGate(ILeadershipGate leadership) : IWorkGate
    {
        public bool IsOpen => leadership.IsActive;
    }
}
