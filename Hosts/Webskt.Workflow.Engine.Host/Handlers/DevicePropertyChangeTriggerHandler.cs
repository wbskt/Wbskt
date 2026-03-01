using System.Text.Json;
using MassTransit;
using Webskt.Events.Shared;
using Webskt.Workflow.Engine.Host.Interfaces;
using Webskt.Workflow.Engine.Host.Models.TriggerContexts;

namespace Webskt.Workflow.Engine.Host.Handlers;

public sealed class DevicePropertyChangeTriggerHandler : IConsumer<DeviceMessageReceivedEvent>
{
    private readonly IWorkflowRuntimeRegistry _registry;
    private readonly IWorkflowEngine _engine;
    private readonly ILogger<DevicePropertyChangeTriggerHandler> _logger;

    public DevicePropertyChangeTriggerHandler(
        IWorkflowRuntimeRegistry registry,
        IWorkflowEngine engine,
        ILogger<DevicePropertyChangeTriggerHandler> logger)
    {
        _registry = registry;
        _engine = engine;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<DeviceMessageReceivedEvent> context)
    {
        // Only handle PropertyChange messages
        // TODO: review
        if (!context.Message.MessageType.Equals("PropertyChange", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var deviceRefId = context.Message.ClientRefId;
        var triggerKey = $"device:{deviceRefId}".ToLowerInvariant();
        
        var workflows = _registry.GetWorkflows(triggerKey);

        if (!workflows.Any())
        {
            return;
        }

        var triggerContext = new DevicePropertyChangeTriggerContext 
        { 
            DeviceRefId = deviceRefId,
            PropertyName = "unknown", // To be refined based on event payload structure
            NewValue = ParsePayload(context.Message.Payload)
        };

        var startTasks = workflows
            .Where(w => w.WorkspaceId == context.Message.WorkspaceId)
            .Select(async workflow =>
            {
                try
                {
                    await _engine.StartAsync(workflow, triggerContext);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to start workflow {WorkflowRefId} for property change from device {ClientRefId}", 
                        workflow.WorkflowRefId, deviceRefId);
                }
            });

        await Task.WhenAll(startTasks);
    }

    private static object? ParsePayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;

        try 
        {
            return JsonSerializer.Deserialize<JsonElement>(payload);
        }
        catch 
        {
            return payload;
        }
    }
}
