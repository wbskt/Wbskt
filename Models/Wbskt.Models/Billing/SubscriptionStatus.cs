using System.Text.Json.Serialization;

namespace Wbskt.Models.Billing;

/// <summary>
/// Where a tenant's subscription with the payment provider stands, as last read from it.
/// <c>PastDue</c> and <c>OnHold</c> keep the paid plan for a grace period (Docs/Billing.md, Payments);
/// <c>Cancelled</c> keeps it until the period paid for ends.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SubscriptionStatus>))]
public enum SubscriptionStatus
{
    Pending,
    Active,
    PastDue,
    OnHold,
    Paused,
    Cancelled,
    Failed,
    Expired
}
