using System.Collections.Concurrent;

namespace Wbskt.Workflow.Engine.Host.V2;

public class InboundManager
{
    // <trigger-key, <nodeRef, IWorkflowRuntime>>
    // trigger-key will be something like "wbh-p:/orders/kochi" or "cp:<guid of the client>"
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, IWorkflowRuntime>> _triggerMap = new();

    public Task TriggerAsync(string triggerKey, object trigger)
    {
        if (!_triggerMap.TryGetValue(triggerKey, out var nodeMap) || nodeMap.IsEmpty)
        {
            return Task.CompletedTask;
        }

        var tasks = nodeMap
            .Select(kvp => kvp.Value.ExecuteTriggerAsync(kvp.Key, trigger))
            .ToList();

        return Task.WhenAll(tasks);
    }

    public bool RegisterInput(string triggerKey, IWorkflowRuntime runtime, Guid nodeRef)
    {
        var nodeMap = _triggerMap.GetOrAdd(triggerKey, _ => new ConcurrentDictionary<Guid, IWorkflowRuntime>());
        nodeMap[nodeRef] = runtime;

        runtime.OnDisposed += () => UnRegisterInput(triggerKey, nodeRef);
        return true;
    }

    private void UnRegisterInput(string triggerKey, Guid nodeRef)
    {
        if (!_triggerMap.TryGetValue(triggerKey, out var nodeMap))
        {
            return;
        }

        nodeMap.TryRemove(nodeRef, out _);

        // Clean up the key entirely when no registrations remain to avoid memory leaks
        if (nodeMap.IsEmpty)
        {
            _triggerMap.TryRemove(triggerKey, out _);
        }
    }
}