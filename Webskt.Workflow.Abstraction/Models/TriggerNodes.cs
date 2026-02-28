using Webskt.Workflow.Abstraction.Enums;

namespace Webskt.Workflow.Abstraction.Models;

public class DeviceTriggerNode : BaseTrigger
{
    public DeviceTriggerNode()
    {
        Ports.Add(new PortDefinition { PortId = "OnTelemetry", Direction = PortDirection.Out });
        Ports.Add(new PortDefinition { PortId = "OnPropertyChange", Direction = PortDirection.Out });
    }

    public Guid ClientRefId { get; set; }
}

public class TimerScheduleNode : BaseTrigger
{
    public TimerScheduleNode()
    {
        Ports.Add(new PortDefinition { PortId = "out", Direction = PortDirection.Out });
    }

    public int Interval { get; set; }
    public ScheduleUnit Unit { get; set; }
}
