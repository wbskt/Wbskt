using System.Text.Json;
using Wbskt.Models.Billing;

namespace Wbskt.Infrastructure.Billing;

/// <summary>How a tenant's <see cref="PlanLimitOverrides"/> are stored: JSON, null when there are none.</summary>
public static class PlanLimitOverridesJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static string? Write(PlanLimitOverrides? overrides) =>
        overrides is null || overrides == new PlanLimitOverrides() ? null : JsonSerializer.Serialize(overrides, Options);

    public static PlanLimitOverrides? Read(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<PlanLimitOverrides>(json, Options);
}
