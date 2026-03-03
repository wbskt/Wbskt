using Webskt.Workflow.Abstraction.Models;
using Webskt.Workflow.Abstraction.Models.Nodes;
using Webskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Webskt.Workflow.Engine.Host.Enums;
using Webskt.Workflow.Engine.Host.Interfaces;
using Webskt.Workflow.Engine.Host.Models;
using Webskt.Workflow.Engine.Host.Models.TriggerContexts;
using Webskt.Workflow.Abstraction.Enums;
using ExecutionContext = Webskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Webskt.Workflow.Engine.Host.Services;

public sealed class WorkflowEngine : IWorkflowEngine
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WorkflowEngine> _logger;

    public WorkflowEngine(IServiceProvider serviceProvider, ILogger<WorkflowEngine> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<WorkflowInstance> StartAsync(WorkflowDefinition definition, BaseTriggerContext triggerContext)
    {
        _logger.LogInformation("Starting workflow {WorkflowName} ({WorkflowRefId})", definition.Name, definition.WorkflowRefId);

        var instance = new WorkflowInstance
        {
            WorkflowRefId = definition.WorkflowRefId,
            WorkspaceId = definition.WorkspaceId,
            TriggerContext = triggerContext,
            State = new Dictionary<string, object?>(definition.InitialState)
        };

        // 1. Find ONLY the trigger nodes that match the context
        var matchingTriggers = definition.Nodes
            .OfType<BaseTrigger>()
            .Where(node => NodeMatchesContext(node, triggerContext))
            .ToList();

        if (matchingTriggers.Count == 0)
        {
            _logger.LogWarning("No matching trigger nodes found for workflow {WorkflowRefId} with context {ContextType}", 
                definition.WorkflowRefId, triggerContext.GetType().Name);
            
            instance.Status = WorkflowStatus.Completed; // Nothing to do
            return instance;
        }

        foreach (var trigger in matchingTriggers)
        {
            var pointer = new ExecutionPointer { NodeId = trigger.NodeId };
            instance.Pointers.Add(pointer);
            
            // Start execution for this trigger branch
            _ = ExecutePointerAsync(instance, pointer, definition);
        }

        return instance;
    }

    public Task ResumeAsync(Guid instanceId)
    {
        // TODO: Load instance from store and find pointers with ResumeAt <= Now
        throw new NotImplementedException("Resume logic requires a persistent InstanceStore.");
    }

    private async Task ExecutePointerAsync(WorkflowInstance instance, ExecutionPointer pointer, WorkflowDefinition definition)
    {
        try
        {
            while (pointer.Status == ExecutionStatus.Active)
            {
                var node = definition.Nodes.FirstOrDefault(n => n.NodeId == pointer.NodeId);
                if (node == null)
                {
                    pointer.Status = ExecutionStatus.Faulted;
                    pointer.ErrorMessage = $"Node {pointer.NodeId} not found in definition.";
                    break;
                }

                // Create the safe execution context
                var context = new ExecutionContext(instance, pointer.PointerId);

                // Resolve the executor for this specific node type
                var executor = ResolveExecutor(node);
                
                var result = await executor.ExecuteAsync(node, context);

                if (!result.IsSuccess)
                {
                    pointer.Status = ExecutionStatus.Faulted;
                    pointer.ErrorMessage = result.ErrorMessage;
                    break;
                }

                if (result.WaitUntil.HasValue)
                {
                    pointer.Status = ExecutionStatus.Waiting;
                    pointer.ResumeAt = result.WaitUntil;
                    break;
                }

                // Node completed successfully. Find next steps.
                pointer.Status = ExecutionStatus.Completed;

                var nextEdges = definition.Edges
                    .Where(e => e.Source.NodeId == node.NodeId && result.ActivatedPortIds.Contains(e.Source.PortId))
                    .ToList();

                if (nextEdges.Count == 0)
                {
                    break;
                }

                // Handle Fan-out: 
                // The FIRST edge reuses the current pointer.
                // Subsequent edges create NEW parallel pointers.
                for (int i = 0; i < nextEdges.Count; i++)
                {
                    var edge = nextEdges[i];
                    if (i == 0)
                    {
                        pointer.NodeId = edge.Target.NodeId;
                        pointer.Status = ExecutionStatus.Active;
                    }
                    else
                    {
                        var newPointer = new ExecutionPointer { NodeId = edge.Target.NodeId };
                        instance.Pointers.Add(newPointer);
                        _ = ExecutePointerAsync(instance, newPointer, definition);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing pointer {PointerId} at node {NodeId}", pointer.PointerId, pointer.NodeId);
            pointer.Status = ExecutionStatus.Faulted;
            pointer.ErrorMessage = ex.Message;
        }
        finally
        {
            CheckWorkflowCompletion(instance);
        }
    }

    private bool NodeMatchesContext(BaseTrigger node, BaseTriggerContext context)
    {
        return (node, context) switch
        {
            (DeviceTriggerNode dtn, ClientPayloadTriggerContext cpc) => 
                dtn.ClientRefId == cpc.DeviceRefId && dtn.TriggerType == DeviceTriggerType.OnTelemetry,
                
            (DeviceTriggerNode dtn, ClientPropertyChangeTriggerContext dpc) => 
                dtn.ClientRefId == dpc.DeviceRefId && dtn.TriggerType == DeviceTriggerType.OnPropertyChange &&
                (string.IsNullOrEmpty(dtn.PropertyName) || dtn.PropertyName == dpc.PropertyName),
                
            // TODO: Add TimerScheduleNode matching
            _ => false
        };
    }

    private IWorkflowNodeExecutor ResolveExecutor(BaseNode node)
    {
        // Use naming convention: [NodeTypeName]Executor
        // e.g. LogicGateNode -> LogicGateExecutor
        var nodeName = node.GetType().Name;
        var executorName = nodeName.Replace("Node", "Executor");
        
        // Find by name in the DI container (using keyed services or assembly scanning)
        // For the MVP, we can use a simple map or type-based resolution
        var executorType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => typeof(IWorkflowNodeExecutor).IsAssignableFrom(t) && t.Name == executorName);

        if (executorType == null)
        {
            throw new InvalidOperationException($"No executor found for node type {nodeName}. Expected {executorName}.");
        }

        return (IWorkflowNodeExecutor)ActivatorUtilities.CreateInstance(_serviceProvider, executorType);
    }

    private void CheckWorkflowCompletion(WorkflowInstance instance)
    {
        lock (instance.Pointers)
        {
            if (instance.Pointers.All(p => p.Status == ExecutionStatus.Completed || p.Status == ExecutionStatus.Faulted))
            {
                instance.Status = WorkflowStatus.Completed;
                instance.FinishedAt = DateTime.UtcNow;
                _logger.LogInformation("Workflow instance {InstanceId} completed.", instance.InstanceId);
            }
        }
    }
}
