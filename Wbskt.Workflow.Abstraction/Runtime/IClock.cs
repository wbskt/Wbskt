namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IClock
{
    DateTime UtcNow { get; }
}
