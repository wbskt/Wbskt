using Webskt.Workflow.Abstraction.Enums;

namespace Webskt.Workflow.Abstraction.Models;

public class ValueTransformationNode : BaseControl
{
    public ValueTransformationNode()
    {
        Ports.Add(new PortDefinition { PortId = "in", Direction = PortDirection.In });
        Ports.Add(new PortDefinition { PortId = "out", Direction = PortDirection.Out });
    }

    public string MappingExpression { get; set; } = string.Empty;
    public string OutputVariableName { get; set; } = string.Empty;
}

public class CodeSnippetNode : BaseControl
{
    public CodeSnippetNode()
    {
        Ports.Add(new PortDefinition { PortId = "in", Direction = PortDirection.In });
        Ports.Add(new PortDefinition { PortId = "out", Direction = PortDirection.Out });
    }

    public string Language { get; set; } = "javascript";
    public string Code { get; set; } = string.Empty;
    public Dictionary<string, string> InputVariables { get; set; } = new();
    public List<string> OutputVariables { get; set; } = new();
}

public class AggregateNode : BaseControl
{
    public AggregateNode()
    {
        Ports.Add(new PortDefinition { PortId = "in", Direction = PortDirection.In });
        Ports.Add(new PortDefinition { PortId = "out", Direction = PortDirection.Out });
    }

    public string Function { get; set; } = "Sum"; // Sum, Average, Min, Max, Count
    public string DataExpression { get; set; } = string.Empty;
    public int TimeWindowSeconds { get; set; }
}

public class VariableNode : BaseControl
{
    public VariableNode()
    {
        Ports.Add(new PortDefinition { PortId = "in", Direction = PortDirection.In });
        Ports.Add(new PortDefinition { PortId = "out", Direction = PortDirection.Out });
    }

    public string VariableName { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
    public string Operation { get; set; } = "Set"; // Set, Get, Increment, Decrement
}
