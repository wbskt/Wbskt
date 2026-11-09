using System.Text.Json;
using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Services.Billing;

/// <summary>
/// A subscription status as the provider writes it and <c>dbo.TenantSubscriptions</c> stores it:
/// snake case, <c>past_due</c> for <see cref="SubscriptionStatus.PastDue"/>.
/// </summary>
public static class SubscriptionStatusNames
{
    public static string ToName(SubscriptionStatus status) => JsonNamingPolicy.SnakeCaseLower.ConvertName(status.ToString());

    public static bool TryParse(string? name, out SubscriptionStatus status)
    {
        foreach (SubscriptionStatus candidate in Enum.GetValues<SubscriptionStatus>())
        {
            if (string.Equals(ToName(candidate), name, StringComparison.OrdinalIgnoreCase))
            {
                status = candidate;
                return true;
            }
        }

        status = default;
        return false;
    }
}
