using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Expressions;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;

public sealed class LoopNode : BaseControl
{
    public sealed override List<PortDefinition> Ports { get; set; } =
    [
        new PortDefinition { PortId = PortNames.In, Direction = PortDirection.In },
        new PortDefinition { PortId = PortNames.Body, Direction = PortDirection.Out },
        new PortDefinition { PortId = PortNames.Completed, Direction = PortDirection.Out }
    ];

    public WorkflowExpression? ItemsExpression { get; set; }
    public string IteratorName { get; set; } = "item";
}