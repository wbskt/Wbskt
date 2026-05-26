using System.Text.Json;

namespace Wbskt.Workflow.Abstraction.Runtime;

public interface INodeExecutionServices
{
    IProviderComposite Providers { get; }

    int Tick { get; }

    IReadOnlyDictionary<string, JsonElement>? ParentResults { get; }
}
