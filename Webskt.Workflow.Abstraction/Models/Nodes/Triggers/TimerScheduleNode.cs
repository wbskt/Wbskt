using Webskt.Workflow.Abstraction.Enums;

namespace Webskt.Workflow.Abstraction.Models.Nodes.Triggers;

public class TimerScheduleNode : BaseTrigger
{
    public sealed override List<PortDefinition> Ports { get; set; } =
    [
        new PortDefinition { PortId = PortNames.Out, Direction = PortDirection.Out }
    ];

    public int Interval { get; set; }
    public ScheduleUnit Unit { get; set; }
}