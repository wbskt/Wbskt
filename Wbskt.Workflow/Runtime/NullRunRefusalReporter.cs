using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class NullRunRefusalReporter : IRunRefusalReporter
{
    public void Report(RunRefusal refusal)
    {
    }
}
