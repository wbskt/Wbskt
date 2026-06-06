using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IRunCompletedPublisher
{
    Task PublishAsync(RunRow run, string terminalStatus, CancellationToken ct);
}
