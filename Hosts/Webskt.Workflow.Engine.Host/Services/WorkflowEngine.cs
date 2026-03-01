using Webskt.Workflow.Abstraction.Models;
using Webskt.Workflow.Abstraction.Models.Nodes;
using Webskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Webskt.Workflow.Engine.Host.Enums;
using Webskt.Workflow.Engine.Host.Interfaces;
using Webskt.Workflow.Engine.Host.Models;
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

    public async Task<WorkflowInstance> StartAsync(WorkflowDefinition definition, object? triggerData)
    {
        _logger.LogInformation("Starting workflow {WorkflowName} ({WorkflowRefId})", definition.Name, definition.WorkflowRefId);

        var instance = new WorkflowInstance
        {
            WorkflowRefId = definition.WorkflowRefId,
            WorkspaceId = definition.WorkspaceId,
            TriggerData = triggerData,
            State = new Dictionary<string, object?>(definition.InitialState)
        };

        // Find all triggers in the definition
        var triggers = definition.Nodes.OfType<BaseTrigger>().ToList();
        
        foreach (var trigger in triggers)
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
                // Note: We'll need to implement this dynamic resolution logic
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

                if (nextEdges.Count == 0) break;

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

    private IWorkflowNodeExecutor ResolveExecutor(BaseNode node)
    {
        // This will be replaced with a proper Registry/Factory
        // For now, it's a placeholder
        throw new NotImplementedException($"Executor for node type {node.GetType().Name} is not registered.");
    }

    private void CheckWorkflowCompletion(WorkflowInstance instance)
    {
        if (instance.Pointers.All(p => p.Status == ExecutionStatus.Completed || p.Status == ExecutionStatus.Faulted))
        {
            instance.Status = WorkflowStatus.Completed;
            instance.FinishedAt = DateTime.UtcNow;
            _logger.LogInformation("Workflow instance {InstanceId} completed.", instance.InstanceId);
        }
    }
}
