using System.Text.Json.Serialization;

namespace Wbskt.Models.Billing;

/// <summary>
/// Who put a tenant on its plan (<c>dbo.Tenants.PlanSource</c>). The payment provider's sync moves only a
/// plan it set itself, or the default every tenant starts on, so an operator's plan is never undone by a
/// renewal.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlanSource>))]
public enum PlanSource
{
    /// <summary>The plan every tenant starts on, never changed.</summary>
    Default,

    /// <summary>Set by the sync from the tenant's subscription with the payment provider.</summary>
    Subscription,

    /// <summary>Set by an operator, for a negotiated contract or a goodwill change; may have an end date.</summary>
    Operator
}
