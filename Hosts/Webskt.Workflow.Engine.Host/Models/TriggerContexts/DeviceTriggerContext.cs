namespace Webskt.Workflow.Engine.Host.Models.TriggerContexts;

public abstract class DeviceTriggerContext : BaseTriggerContext
{
    public required Guid DeviceRefId { get; init; }
}
