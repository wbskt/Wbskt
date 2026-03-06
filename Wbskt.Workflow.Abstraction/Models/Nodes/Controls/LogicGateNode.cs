using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Expressions;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;

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