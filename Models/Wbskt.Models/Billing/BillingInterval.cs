using System.Text.Json.Serialization;

namespace Wbskt.Models.Billing;

/// <summary>How often a paid plan is billed. Sent by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BillingInterval>))]
public enum BillingInterval
{
    Monthly,
    Yearly
}
