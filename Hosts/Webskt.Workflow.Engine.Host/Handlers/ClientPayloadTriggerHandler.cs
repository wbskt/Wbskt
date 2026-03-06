using System.Text.Json;
using MassTransit;
using Webskt.Events.Client;
using Webskt.Workflow.Engine.Host.Interfaces;
using Webskt.Workflow.Engine.Host.Models.TriggerContexts;

namespace Webskt.Workflow.Engine.Host.Handlers;

public sealed class ClientPayloadTriggerHandler : IConsumer<ClientPayloadReceivedEvent>
{
    private readonly IWorkflowRuntimeRegistry _registry;
    private readonly IWorkflowEngine _engine;
    private readonly ILogger<ClientPayloadTriggerHandler> _logger;

    public ClientPayloadTriggerHandler(
        IWorkflowRuntimeRegistry registry,
        IWorkflowEngine engine,
        ILogger<ClientPayloadTriggerHandler> logger)
    {
        _registry = registry;
        _engine = engine;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ClientPayloadReceivedEvent> context)
    {
        _logger.LogTrace("ClientPayloadTriggerHandler triggered with client: {client}, data: {data}", context.Message.ClientRefId, context.Message.Payload);
        // Safety check: ensure it's actually telemetry
        if (!context.Message.MessageType.Equals("Telemetry", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var deviceRefId = context.Message.ClientRefId;
        var triggerKey = $"client:{deviceRefId}".ToLowerInvariant();
        
        var workflows = _registry.GetWorkflows(triggerKey);

        if (!workflows.Any())
        {
            return;
        }

        var triggerContext = new ClientPayloadTriggerContext 
        { 
            DeviceRefId = deviceRefId,
            Data = ParsePayload(context.Message.Payload) 
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
                    _logger.LogError(ex, "Failed to start workflow {WorkflowRefId} for payload from client {ClientRefId}", 
                        workflow.WorkflowRefId, deviceRefId);
                }
            });

        await Task.WhenAll(startTasks);
        _logger.LogTrace("DONE - ClientPayloadTriggerHandler - client: {client}, data: {data}", context.Message.ClientRefId, context.Message.Payload);
    }

    private static object? ParsePayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

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
