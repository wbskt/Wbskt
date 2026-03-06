namespace Wbskt.Workflow.Abstraction.Enums;

public enum BinaryOperator
{
    // Math
    Add = 0,
    Subtract = 1,
    Multiply = 2,
    Divide = 3,
    Modulo = 4,

    // Comparison
    Equal = 10,
    NotEqual = 11,
    GreaterThan = 12,
    GreaterThanOrEqual = 13,
    LessThan = 14,
    LessThanOrEqual = 15,

    // Logic
    And = 20,
    Or = 21,

    // Collection/String
    Contains = 30,
    In = 31
}
