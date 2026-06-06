using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IRunStartedPublisher
{
    Task PublishAsync(RunRow run, CancellationToken ct);
}
