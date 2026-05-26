using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.SharedVariables;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(NoneResetPolicy), "none")]
[JsonDerivedType(typeof(DailyAtUtcResetPolicy), "dailyAtUtc")]
[JsonDerivedType(typeof(OnDefinitionPublishResetPolicy), "onDefinitionPublish")]
public abstract record ResetPolicy;

public sealed record NoneResetPolicy() : ResetPolicy;

public sealed record DailyAtUtcResetPolicy(
    [property: JsonPropertyName("time")] TimeSpan Time
) : ResetPolicy;

public sealed record OnDefinitionPublishResetPolicy() : ResetPolicy;
