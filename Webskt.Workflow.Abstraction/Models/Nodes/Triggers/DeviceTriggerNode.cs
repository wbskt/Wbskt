using Webskt.Workflow.Abstraction.Enums;

namespace Webskt.Workflow.Abstraction.Models.Nodes.Triggers;

public class DeviceTriggerNode : BaseTrigger
{
    public sealed override List<PortDefinition> Ports { get; set; } =
    [
        new PortDefinition { PortId = PortNames.OnTelemetry, Direction = PortDirection.Out },
        new PortDefinition { PortId = PortNames.OnPropertyChange, Direction = PortDirection.Out }
    ];

    public Guid ClientRefId { get; set; }
}