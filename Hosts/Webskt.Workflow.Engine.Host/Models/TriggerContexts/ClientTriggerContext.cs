namespace Webskt.Workflow.Engine.Host.Models.TriggerContexts;

public abstract class ClientTriggerContext : BaseTriggerContext
{
    public required Guid DeviceRefId { get; init; }
}
