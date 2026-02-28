using Webskt.Workflow.Abstraction.Enums;

namespace Webskt.Workflow.Abstraction.Models.Nodes.Controls;

public sealed class VariableNode : BaseControl
{
    public sealed override List<PortDefinition> Ports { get; set; } =
    [
        new PortDefinition { PortId = PortNames.In, Direction = PortDirection.In },
        new PortDefinition { PortId = PortNames.Out, Direction = PortDirection.Out }
    ];

    public string VariableName { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
    public string Operation { get; set; } = "Set"; // Set, Get, Increment, Decrement
}