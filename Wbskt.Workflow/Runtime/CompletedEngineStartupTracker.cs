using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class CompletedEngineStartupTracker : IEngineStartupTracker
{
    public Task Ready => Task.CompletedTask;
}
