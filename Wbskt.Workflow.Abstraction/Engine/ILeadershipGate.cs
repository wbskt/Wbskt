namespace Wbskt.Workflow.Abstraction.Engine;

/// <summary>
/// Whether this instance is the active engine. The engine runs active/standby: one instance holds the
/// engine-leader lease, starts the bus and runs every periodic job; the others wait to take over. Each
/// job asks this one question before it works, rather than holding a lease of its own.
/// </summary>
public interface ILeadershipGate
{
    bool IsActive { get; }
}
