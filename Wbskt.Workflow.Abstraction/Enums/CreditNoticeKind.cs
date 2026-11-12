namespace Wbskt.Workflow.Abstraction.Enums;

/// <summary>What a <see cref="Entities.CreditNotice"/> is measured against.</summary>
public enum CreditNoticeKind
{
    /// <summary>A workspace's monthly credit cap.</summary>
    Cap,

    /// <summary>A tenant's plan's monthly credit allowance.</summary>
    Allowance
}
