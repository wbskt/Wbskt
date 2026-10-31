using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Templates;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record EmailNotificationNode : BaseActionNode, IIntegrationStep, ITemplatedStep
{
    [JsonPropertyName("config")]
    public required EmailConfig Config { get; init; }

    [JsonIgnore]
    object? ITemplatedStep.TemplatedConfig => Config;


    [JsonIgnore]
    public Guid? IntegrationRef => Config?.IntegrationRef is { } r && r != Guid.Empty ? r : null;

    [JsonIgnore]
    public override string Kind => NodeKind.ActionEmail;
}
