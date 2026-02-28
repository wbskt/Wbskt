using Webskt.Workflow.Abstraction.Enums;
using Webskt.Workflow.Abstraction.Models.Expressions;

namespace Webskt.Workflow.Abstraction.Models.Nodes.Controls;

public sealed class LogicGateNode : BaseControl
{
    public sealed override List<PortDefinition> Ports { get; set; } =
    [
        new PortDefinition { PortId = PortNames.In, Direction = PortDirection.In },
        new PortDefinition { PortId = PortNames.Match, Direction = PortDirection.Out },
        new PortDefinition { PortId = PortNames.Otherwise, Direction = PortDirection.Out }
    ];

    public required WorkflowExpression Condition { get; set; }
}