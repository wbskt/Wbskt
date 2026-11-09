namespace Wbskt.Models.Billing;

/// <summary>How many of something an account has against what its plan allows.</summary>
public sealed record UsageCount(int Used, int Limit);
