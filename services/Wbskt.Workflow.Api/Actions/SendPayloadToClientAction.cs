using Wbskt.Common.Configurations;
using Wbskt.Common.Events;
using Wbskt.EventBus;
using Wbskt.Workflow.Api.Core;
using Wbskt.Workflow.Api.Core.Abstractions;

namespace Wbskt.Workflow.Api.Actions;

public class SendPayloadToClientAction : IAction
{
    private readonly IEventBus _eventBus;

    public SendPayloadToClientAction(IEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public async Task<ActionResult> ExecuteAsync(StepConfigurationBase configuration, WorkflowContext context, CancellationToken cancellationToken)
    {
        // In a real implementation, these would be read from the StepConfiguration
        var clientId = (int)context.Properties["client_id"];
        var payload = "Hello from workflow!";

        var command = new SendCommandToClientEvent(clientId, payload);
        await _eventBus.PublishAsync(command, cancellationToken);

        return new ActionResult(true);
    }
}
