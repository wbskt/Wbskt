using Webskt.Workflow.Abstraction.Enums;

namespace Webskt.Workflow.Abstraction.Models.Nodes.Actions;

public sealed class WebhookNotificationNode : BaseAction
{
    public sealed override List<PortDefinition> Ports { get; set; } =
    [
        new PortDefinition { PortId = PortNames.In, Direction = PortDirection.In },
        new PortDefinition { PortId = PortNames.OnResponse, Direction = PortDirection.Out }
    ];

    public string Url { get; set; } = string.Empty;
    public string Method { get; set; } = "POST";
    public string Payload { get; set; } = string.Empty;
}