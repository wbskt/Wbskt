using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Abstraction.Models;

public class PortDefinition
{
    public string PortId { get; set; } = PortNames.Out;
    public PortDirection Direction { get; set; } = PortDirection.Out;
}