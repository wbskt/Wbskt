using Webskt.EventBus.Abstractions;
using Webskt.Events.Workflow;
using Webskt.Workflow.Abstraction.Enums;
using Webskt.Workflow.Abstraction.Models;
using Webskt.Workflow.Abstraction.Models.Nodes;
using Webskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Webskt.Workflow.Engine.Host.Enums;
using Webskt.Workflow.Engine.Host.Interfaces;
using Webskt.Workflow.Engine.Host.Models;
using Webskt.Workflow.Engine.Host.Models.TriggerContexts;
using ExecutionContext = Webskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Webskt.Workflow.Engine.Host.Services;

public sealed class WorkflowEngine : IWorkflowEngine
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEventBus _eventBus;
    private readonly ILogger<WorkflowEngine> _logger;

    public WorkflowEngine(
        IServiceProvider serviceProvider, 
        IEventBus eventBus,
        ILogger<WorkflowEngine> logger)
    {
        _serviceProvider = serviceProvider;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<WorkflowInstance> StartAsync(WorkflowDefinition definition, BaseTriggerContext triggerContext)
    {
        _logger.LogDebug("Starting workflow {WorkflowName} ({WorkflowRefId})", definition.Name, definition.WorkflowRefId);

        var instance = new WorkflowInstance
        {
            WorkflowRefId = definition.WorkflowRefId,
            WorkspaceId = definition.WorkspaceId,
            TriggerContext = triggerContext,
            State = new Dictionary<string, object?>(definition.InitialState)
        };

        await _eventBus.PublishAsync(new WorkflowInstanceStartedEvent(
            instance.InstanceId, instance.WorkflowRefId, instance.WorkspaceId));

        var matchingTriggers = definition.Nodes
            .OfType<BaseTrigger>()
            .Where(node => NodeMatchesContext(node, triggerContext))
            .ToList();

        if (matchingTriggers.Count == 0)
        {
            _logger.LogWarning("No matching trigger nodes found for workflow {WorkflowRefId} with context {ContextType}", 
                definition.WorkflowRefId, triggerContext.GetType().Name);
            
            await CompleteWorkflowAsync(instance);
            return instance;
        }

        foreach (var trigger in matchingTriggers)
        {
            var pointer = new ExecutionPointer { NodeId = trigger.NodeId };
            instance.Pointers.Add(pointer);
            
            _ = ExecutePointerAsync(instance, pointer, definition);
        }

        return instance;
    }

    public Task ResumeAsync(Guid instanceId)
    {
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
                    await FailPointerAsync(instance, pointer, $"Node {pointer.NodeId} not found.");
                    break;
                }

                var context = new ExecutionContext(instance, pointer.PointerId);
                var executor = ResolveExecutor(node);
                
                await _eventBus.PublishAsync(new NodeExecutionStartedEvent(
                    instance.WorkspaceId, instance.InstanceId, pointer.PointerId, node.NodeId, node.GetType().Name, node.Name));

                var result = await executor.ExecuteAsync(node, context);

                if (!result.IsSuccess)
                {
                    await FailPointerAsync(instance, pointer, result.ErrorMessage ?? "Unknown node error.");
                    break;
                }

                if (result.WaitUntil.HasValue)
                {
                    pointer.Status = ExecutionStatus.Waiting;
                    pointer.ResumeAt = result.WaitUntil;
                    break;
                }

                await _eventBus.PublishAsync(new NodeExecutionCompletedEvent(
                    instance.WorkspaceId, instance.InstanceId, pointer.PointerId, node.NodeId, result.ActivatedPortIds));

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
            await FailPointerAsync(instance, pointer, ex.Message);
        }
        finally
        {
            await CheckWorkflowCompletionAsync(instance);
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
        var executor = _serviceProvider.GetKeyedService<IWorkflowNodeExecutor>(node.GetType().Name);
        
        if (executor == null)
        {
            throw new InvalidOperationException($"No executor registered for node type {node.GetType().Name}");
        }

        return executor;
    }

    private async Task FailPointerAsync(WorkflowInstance instance, ExecutionPointer pointer, string message)
    {
        pointer.Status = ExecutionStatus.Faulted;
        pointer.ErrorMessage = message;
        
        await _eventBus.PublishAsync(new NodeExecutionFailedEvent(
            instance.WorkspaceId, instance.InstanceId, pointer.PointerId, pointer.NodeId, message));
    }

    private async Task CheckWorkflowCompletionAsync(WorkflowInstance instance)
    {
        bool isComplete;
        lock (instance.Pointers)
        {
            isComplete = instance.Pointers.All(p => p.Status == ExecutionStatus.Completed || p.Status == ExecutionStatus.Faulted);
        }

        if (isComplete)
        {
            await CompleteWorkflowAsync(instance);
        }
    }

    private async Task CompleteWorkflowAsync(WorkflowInstance instance)
    {
        instance.Status = WorkflowStatus.Completed;
        instance.FinishedAt = DateTime.UtcNow;

        await _eventBus.PublishAsync(new WorkflowInstanceCompletedEvent(
            instance.InstanceId, instance.WorkflowRefId, instance.WorkspaceId, instance.Status.ToString()));
    }
}
