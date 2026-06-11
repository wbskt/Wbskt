using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class NullRunCompletedPublisher : IRunCompletedPublisher
{
    public Task PublishAsync(RunRow run, string terminalStatus, CancellationToken ct)
    {
        _ = run;
        _ = terminalStatus;
        _ = ct;
        return Task.CompletedTask;
    }
}
