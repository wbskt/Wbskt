using MassTransit;
using Webskt.Events.Management;
using Webskt.Workflow.Engine.Host.Interfaces;

namespace Webskt.Workflow.Engine.Host.Handlers;

public sealed class WorkflowDefinitionChangedHandler : 
    IConsumer<WorkflowCreatedEvent>,
    IConsumer<WorkflowUpdatedEvent>,
    IConsumer<WorkflowDeletedEvent>
{
    private readonly IWorkflowRuntimeRegistry _registry;
    private readonly IWorkflowDefinitionProvider _provider;
    private readonly ILogger<WorkflowDefinitionChangedHandler> _logger;

    public WorkflowDefinitionChangedHandler(
        IWorkflowRuntimeRegistry registry,
        IWorkflowDefinitionProvider provider,
        ILogger<WorkflowDefinitionChangedHandler> logger)
    {
        _registry = registry;
        _provider = provider;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<WorkflowCreatedEvent> context)
    {
        _logger.LogInformation("Workflow Created event received for {WorkflowRefId}", context.Message.WorkflowRefId);

        try
        {
            var definition = await _provider.GetByRefIdAsync(context.Message.WorkflowRefId);
            _registry.RegisterWorkflow(definition);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register new workflow {WorkflowRefId} in runtime registry", context.Message.WorkflowRefId);
        }
    }

    public async Task Consume(ConsumeContext<WorkflowUpdatedEvent> context)
    {
        _logger.LogInformation("Workflow Updated event received for {WorkflowRefId}", context.Message.WorkflowRefId);

        try
        {
            var definition = await _provider.GetByRefIdAsync(context.Message.WorkflowRefId);
            _registry.RegisterWorkflow(definition);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update workflow {WorkflowRefId} in runtime registry", context.Message.WorkflowRefId);
        }
    }

    public Task Consume(ConsumeContext<WorkflowDeletedEvent> context)
    {
        _logger.LogInformation("Workflow Deleted event received for {WorkflowRefId}", context.Message.WorkflowRefId);

        _registry.UnregisterWorkflow(context.Message.WorkflowRefId);

        return Task.CompletedTask;
    }
}
