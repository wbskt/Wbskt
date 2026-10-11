using System.Text.Json;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed record NodeExecutionServices(
    IIdempotencyKeyProvider IdempotencyKeys,
    int Tick = 1,
    IReadOnlyDictionary<string, JsonElement>? ParentResults = null) : INodeExecutionServices;
