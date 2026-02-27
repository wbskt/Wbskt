namespace Webskt.Workflow.Abstraction.Models;

public abstract class BaseNode
{
    public Guid NodeId { get; set; }
}

public abstract class BaseTrigger : BaseNode
{
}

public abstract class BaseAction : BaseNode
{
}
