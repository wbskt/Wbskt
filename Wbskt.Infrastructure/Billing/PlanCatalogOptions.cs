using Wbskt.Models.Billing;

namespace Wbskt.Infrastructure.Billing;

/// <summary>
/// Operator changes to the built-in plans (<see cref="PlanCatalog.BuiltIn"/>), bound from
/// <see cref="SectionName"/>: <c>Billing:Plans:Free:Clients = 25</c> reprices one limit of one plan
/// without a release. Anything left out keeps the built-in value.
/// </summary>
public sealed class PlanCatalogOptions
{
    public const string SectionName = "Billing:Plans";

    public Dictionary<PlanCode, PlanLimitOverrides> Plans { get; init; } = [];
}
