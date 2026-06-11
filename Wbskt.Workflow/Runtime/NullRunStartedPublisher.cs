using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class NullRunStartedPublisher : IRunStartedPublisher
{
    public Task PublishAsync(RunRow run, CancellationToken ct)
    {
        _ = run;
        _ = ct;
        return Task.CompletedTask;
    }
}
