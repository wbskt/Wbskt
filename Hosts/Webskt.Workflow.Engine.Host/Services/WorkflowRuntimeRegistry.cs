using System.Collections.Concurrent;
using Webskt.Workflow.Abstraction.Models;
using Webskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Webskt.Workflow.Engine.Host.Interfaces;

namespace Webskt.Workflow.Engine.Host.Services;

public sealed class WorkflowRuntimeRegistry : IWorkflowRuntimeRegistry
{
    // Key: TriggerKey (e.g. "client:guid"), Value: Set of WorkflowRefIds
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, byte>> _triggerMap = new();
    
    // Key: WorkflowRefId, Value: The actual definition
    private readonly ConcurrentDictionary<Guid, WorkflowDefinition> _definitions = new();

    // Key: WorkflowRefId, Value: List of trigger keys it registered (for fast pruning)
    private readonly ConcurrentDictionary<Guid, List<string>> _workflowToTriggerKeys = new();

    private readonly ILogger<WorkflowRuntimeRegistry> _logger;

    public WorkflowRuntimeRegistry(ILogger<WorkflowRuntimeRegistry> logger)
    {
        _logger = logger;
    }

    public IReadOnlyCollection<WorkflowDefinition> GetWorkflows(string triggerKey)
    {
        var result = new List<WorkflowDefinition>();

        if (_triggerMap.TryGetValue(triggerKey, out var workflowIds))
        {
            foreach (var id in workflowIds.Keys)
            {
                if (_definitions.TryGetValue(id, out var definition) && definition.IsEnabled)
                {
                    result.Add(definition);
                }
            }
        }

        return result.AsReadOnly();
    }

    public void RegisterWorkflow(WorkflowDefinition definition)
    {
        _logger.LogInformation("Registering workflow {WorkflowRefId} in runtime registry", definition.WorkflowRefId);

        // 1. Remove old version if it exists (uses the fast reverse-map pruning)
        UnregisterWorkflow(definition.WorkflowRefId);

        // 2. Store the new definition
        _definitions[definition.WorkflowRefId] = definition;

        // 3. Map all its triggers
        var triggerKeys = ExtractTriggerKeys(definition).ToList();
        _workflowToTriggerKeys[definition.WorkflowRefId] = triggerKeys;

        foreach (var key in triggerKeys)
        {
            var ids = _triggerMap.GetOrAdd(key, _ => new ConcurrentDictionary<Guid, byte>());
            ids.TryAdd(definition.WorkflowRefId, 0);
        }
    }

    public void UnregisterWorkflow(Guid workflowRefId)
    {
        // 1. Remove from definitions
        _definitions.TryRemove(workflowRefId, out _);

        // 2. Fast pruning using the reverse map
        if (_workflowToTriggerKeys.TryRemove(workflowRefId, out var keys))
        {
            foreach (var key in keys)
            {
                if (_triggerMap.TryGetValue(key, out var ids))
                {
                    ids.TryRemove(workflowRefId, out _);
                    
                    // Optional: Clean up empty keys to save memory
                    if (ids.IsEmpty)
                    {
                        _triggerMap.TryRemove(key, out _);
                    }
                }
            }
        }
    }

    public void Initialize(IEnumerable<WorkflowDefinition> definitions)
    {
        _logger.LogInformation("Initializing runtime registry...");
        
        _triggerMap.Clear();
        _definitions.Clear();
        _workflowToTriggerKeys.Clear();

        foreach (var def in definitions)
        {
            RegisterWorkflow(def);
        }
    }

    private static IEnumerable<string> ExtractTriggerKeys(WorkflowDefinition definition)
    {
        foreach (var node in definition.Nodes)
        {
            switch (node)
            {
                case DeviceTriggerNode deviceTrigger:
                    yield return $"client:{deviceTrigger.ClientRefId}".ToLowerInvariant();
                    break;
                case TimerScheduleNode timerTrigger:
                    yield return "timer:system";
                    break;
            }
        }
    }
}
