using Webskt.Workflow.Abstraction.Enums;

namespace Webskt.Workflow.Abstraction.Models.Nodes.Actions;

public class SendCommandActionNode : BaseAction
{
    public sealed override List<PortDefinition> Ports { get; set; } =
    [
        new PortDefinition { PortId = PortNames.In, Direction = PortDirection.In },
        new PortDefinition { PortId = PortNames.Out, Direction = PortDirection.Out }
    ];

    public Guid TargetClientRefId { get; set; }
    public string ActionName { get; set; } = string.Empty;
    public string PayloadTemplate { get; set; } = string.Empty;
}