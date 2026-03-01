namespace Webskt.Workflow.Engine.Host.Models.TriggerContexts;

public sealed class DevicePropertyChangeTriggerContext : DeviceTriggerContext
{
    public required string PropertyName { get; init; }

    /// <summary>
    /// The new value of the property.
    /// </summary>
    public object? NewValue { get; init; }
}
