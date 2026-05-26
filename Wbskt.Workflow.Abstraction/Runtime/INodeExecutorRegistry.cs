namespace Wbskt.Workflow.Abstraction.Runtime;

public interface INodeExecutorRegistry
{
    INodeExecutor For(string kind);
}
