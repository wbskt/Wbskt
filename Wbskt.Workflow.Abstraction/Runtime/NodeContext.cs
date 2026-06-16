using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Nodes;

namespace Wbskt.Workflow.Abstraction.Runtime;

public sealed record NodeContext
{
    public required BranchContext Branch { get; init; }

    public required BaseNode Node { get; init; }

    public required IProviderComposite Providers { get; init; }

    public required int Tick { get; init; }

    public required IReadOnlyDictionary<string, JsonElement>? ParentResults { get; init; }

    public required CancellationToken CancellationToken { get; init; }

    public required string IdempotencyKey { get; init; }
}
