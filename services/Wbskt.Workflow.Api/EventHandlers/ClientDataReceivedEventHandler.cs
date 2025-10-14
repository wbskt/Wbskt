using Wbskt.Common.Enums;
using Wbskt.Common.Events;
using Wbskt.Common.Readers;
using Wbskt.EventBus;
using Wbskt.Workflow.Api.Core;
using Wbskt.Workflow.Api.Core.Abstractions;

namespace Wbskt.Workflow.Api.EventHandlers;

public class ClientDataReceivedEventHandler : IEventHandler<ClientDataReceivedEvent>
{
    private readonly IWorkflowsReader _workflowsReader;
    private readonly IWorkflowEngine _workflowEngine;
    private readonly ILogger<ClientDataReceivedEventHandler> _logger;

    public ClientDataReceivedEventHandler(
        IWorkflowsReader workflowsReader,
        IWorkflowEngine workflowEngine,
        ILogger<ClientDataReceivedEventHandler> logger)
    {
        _workflowsReader = workflowsReader;
        _workflowEngine = workflowEngine;
        _logger = logger;
    }

    public async Task HandleAsync(ClientDataReceivedEvent @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling client data event for ClientId: {ClientId}", @event.ClientId);

        var workflows = await _workflowsReader.GetActiveWorkflowsByTriggerTypeAsync(TriggerType.ClientData, cancellationToken);

        // In the future, we can filter by client ID or other properties here.
        foreach (var workflow in workflows)
        {
            // For now, we trigger all workflows of this type.
            // A proper condition engine will be built later.
            _logger.LogInformation("Triggering workflow {WorkflowRefId} for client data event.", workflow.RefId);

            var initialData = new Dictionary<string, object>
            {
                ["trigger_type"] = "ClientData",
                ["client_id"] = @event.ClientId,
                ["client_unique_id"] = @event.ClientUniqueId,
                ["payload"] = @event.Payload
            };
            var context = new WorkflowContext(Guid.NewGuid(), initialData);

            _ = _workflowEngine.ExecuteWorkflowAsync(workflow.RefId, context);
        }
    }
}
