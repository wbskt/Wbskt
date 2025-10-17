namespace Wbskt.Common.Configurations;

public class TimedTriggerConfiguration : StepConfigurationBase
{
    public required string CronExpression { get; set; }
}
