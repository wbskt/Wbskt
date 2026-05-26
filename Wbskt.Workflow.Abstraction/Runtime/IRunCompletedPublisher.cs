namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IRunCompletedPublisher
{
    Task PublishAsync(long runId, string terminalStatus, CancellationToken ct);
}
