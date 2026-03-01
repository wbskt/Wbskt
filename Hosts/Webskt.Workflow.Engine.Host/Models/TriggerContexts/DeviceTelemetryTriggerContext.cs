namespace Webskt.Workflow.Engine.Host.Models.TriggerContexts;

public sealed class DeviceTelemetryTriggerContext : DeviceTriggerContext
{
    /// <summary>
    /// The actual telemetry data (JsonElement or raw string).
    /// </summary>
    public object? Data { get; init; }
}
