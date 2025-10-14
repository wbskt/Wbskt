namespace Wbskt.Common.Configurations;

public class IfConditionConfiguration : StepConfigurationBase
{
    public required string LeftOperand { get; set; }
    public required string Operator { get; set; }
    public required string RightOperand { get; set; }
}
