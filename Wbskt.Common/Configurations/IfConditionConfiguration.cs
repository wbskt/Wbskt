using Wbskt.Common.Enums;

namespace Wbskt.Common.Configurations;

// The main configuration for an "If" step.
// It contains a list of condition groups.
public class IfConditionConfiguration : StepConfigurationBase
{
    public LogicalOperatorType LogicalOperator { get; set; } = LogicalOperatorType.And;
    public required List<ConditionGroup> Conditions { get; set; }
}

// Represents a single condition or a nested group of conditions.
public class ConditionGroup
{
    // EITHER a simple condition...
    public SimpleCondition? Condition { get; set; }

    // OR a nested group.
    public IfConditionConfiguration? NestedGroup { get; set; }
}

// Represents a single, atomic comparison.
public class SimpleCondition
{
    public required string LeftOperand { get; set; }
    public required OperatorType Operator { get; set; }
    public required string RightOperand { get; set; }
    public DataType ComparisonType { get; set; } = DataType.String;
}
