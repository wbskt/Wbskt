using Webskt.Workflow.Abstraction.Enums;

namespace Webskt.Workflow.Abstraction.Models;

public class DeviceTriggerNode : BaseTrigger
{
    public Guid ClientRefId { get; set; }
    public DeviceTriggerEvent TriggerEvent { get; set; }
}

public class TimerScheduleNode : BaseTrigger
{
    public int Interval { get; set; }
    public ScheduleUnit Unit { get; set; }
}
