using Webskt.Workflow.Abstraction.Enums;

namespace Webskt.Workflow.Abstraction.Models.Nodes.Controls;

public class ValueTransformationNode : BaseControl
{
    public sealed override List<PortDefinition> Ports { get; set; } =
    [
        new PortDefinition { PortId = PortNames.In, Direction = PortDirection.In },
        new PortDefinition { PortId = PortNames.Out, Direction = PortDirection.Out }
    ];

    public string MappingExpression { get; set; } = string.Empty;
    public string OutputVariableName { get; set; } = string.Empty;
}