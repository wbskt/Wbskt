using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;

public sealed class DeviceTriggerNode : BaseTriggerNode
{
    public sealed override List<PortDefinition> Ports { get; set; } =
    [
        new PortDefinition { PortId = PortNames.Out, Direction = PortDirection.Out }
    ];

    public Guid ClientRefId { get; set; }
    
    public DeviceTriggerType TriggerType { get; set; } = DeviceTriggerType.OnTelemetry;

    /// <summary>
    /// If TriggerType is OnPropertyChange, specify which property to watch.
    /// Leave empty to trigger on ANY property change.
    /// </summary>
    public string PropertyName { get; set; } = string.Empty;
}
