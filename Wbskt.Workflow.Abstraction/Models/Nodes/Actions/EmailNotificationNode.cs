using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record EmailNotificationNode : BaseActionNode
{
    [JsonPropertyName("config")]
    public required EmailConfig Config { get; init; }


    [JsonIgnore]
    public override string Kind => NodeKind.ActionEmail;
}
