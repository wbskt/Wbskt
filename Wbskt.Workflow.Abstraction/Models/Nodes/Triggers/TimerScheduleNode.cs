using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;

public sealed class TimerScheduleNode : BaseTriggerNode
{
    public sealed override List<PortDefinition> Ports { get; set; } =
    [
        new PortDefinition { PortId = PortNames.Out, Direction = PortDirection.Out }
    ];

    public int Interval { get; set; }
    public ScheduleUnit Unit { get; set; }
}