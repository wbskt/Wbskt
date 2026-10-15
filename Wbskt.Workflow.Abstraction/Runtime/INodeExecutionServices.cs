using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Abstraction.Runtime;

public interface INodeExecutionServices
{
    IIdempotencyKeyProvider IdempotencyKeys { get; }
}
