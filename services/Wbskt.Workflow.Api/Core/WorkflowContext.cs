namespace Wbskt.Workflow.Api.Core;

// Holds the data that flows through a workflow execution.
public class WorkflowContext
{
    public Guid WorkflowExecutionId { get; }
    public Dictionary<string, object> Properties { get; }

    public WorkflowContext(Guid executionId, Dictionary<string, object> initialProperties)
    {
        WorkflowExecutionId = executionId;
        Properties = initialProperties;
    }
}
