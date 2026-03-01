using System.Text.Json;
using MassTransit;
using Webskt.Events.Client;
using Webskt.Workflow.Engine.Host.Interfaces;
using Webskt.Workflow.Engine.Host.Models.TriggerContexts;

namespace Webskt.Workflow.Engine.Host.Handlers;

public sealed class ClientPropertyChangeTriggerHandler : IConsumer<ClientPropertyUpdatedEvent>
{
    private readonly IWorkflowRuntimeRegistry _registry;
    private readonly IWorkflowEngine _engine;
    private readonly ILogger<ClientPropertyChangeTriggerHandler> _logger;

    public ClientPropertyChangeTriggerHandler(
        IWorkflowRuntimeRegistry registry,
        IWorkflowEngine engine,
        ILogger<ClientPropertyChangeTriggerHandler> logger)
    {
        _registry = registry;
        _engine = engine;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ClientPropertyUpdatedEvent> context)
    {
        var deviceRefId = context.Message.ClientRefId;
        var triggerKey = $"device:{deviceRefId}".ToLowerInvariant();
        
        var workflows = _registry.GetWorkflows(triggerKey);

        if (!workflows.Any())
        {
            return;
        }

        var triggerContext = new ClientPropertyChangeTriggerContext 
        { 
            DeviceRefId = deviceRefId,
            PropertyName = context.Message.PropertyName,
            NewValue = ParsePayload(context.Message.NewValue)
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
