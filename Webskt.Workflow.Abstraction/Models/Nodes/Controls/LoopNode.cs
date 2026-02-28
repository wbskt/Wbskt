using Webskt.Workflow.Abstraction.Enums;

namespace Webskt.Workflow.Abstraction.Models.Nodes.Controls;

public sealed class LoopNode : BaseControl
{
    public sealed override List<PortDefinition> Ports { get; set; } =
    [
        new PortDefinition { PortId = PortNames.In, Direction = PortDirection.In },
        new PortDefinition { PortId = PortNames.Body, Direction = PortDirection.Out },
        new PortDefinition { PortId = PortNames.Completed, Direction = PortDirection.Out }
    ];

    public string ItemsExpression { get; set; } = string.Empty;
    public string IteratorName { get; set; } = "item";
}