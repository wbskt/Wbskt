using Webskt.Workflow.Abstraction.Enums;

namespace Webskt.Workflow.Abstraction.Models.Nodes.Controls;

public class CodeSnippetNode : BaseControl
{
    public sealed override List<PortDefinition> Ports { get; set; } =
    [
        new PortDefinition { PortId = PortNames.In, Direction = PortDirection.In },
        new PortDefinition { PortId = PortNames.Out, Direction = PortDirection.Out }
    ];

    public string Language { get; set; } = "javascript";
    public string Code { get; set; } = string.Empty;
    public Dictionary<string, string> InputVariables { get; set; } = new();
    public List<string> OutputVariables { get; set; } = new();
}