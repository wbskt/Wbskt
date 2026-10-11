using System.Text.Json;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Abstraction.Runtime;

public interface INodeExecutionServices
{
    IIdempotencyKeyProvider IdempotencyKeys { get; }

    int Tick { get; }

    IReadOnlyDictionary<string, JsonElement>? ParentResults { get; }
}
