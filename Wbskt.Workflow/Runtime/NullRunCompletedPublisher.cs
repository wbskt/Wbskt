using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

public sealed class NullRunCompletedPublisher : IRunCompletedPublisher
{
    public Task PublishAsync(long runId, string terminalStatus, CancellationToken ct)
    {
        _ = runId;
        _ = terminalStatus;
        _ = ct;
        return Task.CompletedTask;
    }
}
