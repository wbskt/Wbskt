using System.Text.Json;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed record NodeExecutionServices(
    IProviderComposite Providers,
    int Tick = 1,
    IReadOnlyDictionary<string, JsonElement>? ParentResults = null) : INodeExecutionServices;
