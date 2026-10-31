namespace Wbskt.Workflow.Abstraction.Entities;

/// <summary>A charge the run's budget accepted: the run's total spend after it, and the budget.</summary>
public sealed record CreditCharge(decimal Spent, decimal Budget);
