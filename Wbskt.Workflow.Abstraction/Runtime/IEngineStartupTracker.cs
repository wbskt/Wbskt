namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IEngineStartupTracker
{
    Task Ready { get; }
}
