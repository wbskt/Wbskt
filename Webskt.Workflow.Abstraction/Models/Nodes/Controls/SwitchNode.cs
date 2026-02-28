using Webskt.Workflow.Abstraction.Enums;

namespace Webskt.Workflow.Abstraction.Models.Nodes.Controls;

public class SwitchNode : BaseControl
{
    private List<PortDefinition> _basePorts = 
    [
        new PortDefinition { PortId = PortNames.In, Direction = PortDirection.In },
        new PortDefinition { PortId = PortNames.Default, Direction = PortDirection.Out }
    ];

    public string Expression { get; set; } = string.Empty;

    public List<string> Cases { get; set; } = new();

    public sealed override List<PortDefinition> Ports
    {
        get
        {
            var allPorts = new List<PortDefinition>(_basePorts);
            foreach (var @case in Cases)
            {
                allPorts.Add(new PortDefinition { PortId = @case, Direction = PortDirection.Out });
            }
            return allPorts;
        }
        set => _basePorts = value;
    }
}