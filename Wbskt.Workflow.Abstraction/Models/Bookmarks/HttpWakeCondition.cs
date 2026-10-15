using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Bookmarks;

public sealed record HttpWakeCondition(
    [property: JsonPropertyName("token")] string Token
) : WakeCondition
{
    [JsonIgnore]
    public override string Kind => "http";
}
