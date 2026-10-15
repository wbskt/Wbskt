using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed record NodeExecutionServices(IIdempotencyKeyProvider IdempotencyKeys) : INodeExecutionServices;
