using System.Collections.Frozen;
using Wbskt.Workflow.Abstraction.Exceptions;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class NodeExecutorRegistry : INodeExecutorRegistry
{
    private readonly FrozenDictionary<string, INodeExecutor> _executors;

    public NodeExecutorRegistry(IEnumerable<INodeExecutor> executors)
    {
        var dictionary = new Dictionary<string, INodeExecutor>(StringComparer.Ordinal);
        foreach (var executor in executors)
        {
            if (!dictionary.TryAdd(executor.Kind, executor))
            {
                throw new InvalidOperationException($"Duplicate executor for {executor.Kind}");
            }
        }

        _executors = dictionary.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public INodeExecutor For(string kind)
    {
        if (_executors.TryGetValue(kind, out var executor))
        {
            return executor;
        }

        throw new NodeKindNotSupportedException(kind);
    }
}
