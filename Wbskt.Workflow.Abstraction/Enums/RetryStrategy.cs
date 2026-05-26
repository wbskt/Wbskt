namespace Wbskt.Workflow.Abstraction.Enums;

public enum RetryStrategy
{
    Constant = 0,
    None = Constant,
    Linear = 1,
    Exponential = 2
}
