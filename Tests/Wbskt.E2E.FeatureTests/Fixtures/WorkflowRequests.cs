using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.E2E.FeatureTests.Fixtures;

/// <summary>The publish and validate bodies for a definition a scenario built: only what an author sends.</summary>
public static class WorkflowRequests
{
    public static WorkflowPublishRequest Publish(Guid refId, string name, WorkflowDefinition definition, bool enable = true) =>
        new(refId, name, definition.Description, definition.Nodes, definition.Edges, enable, definition.SharedVariableSchema, definition.CreditBudget, definition.FailFast);

    public static WorkflowValidationRequest Validate(WorkflowDefinition definition) =>
        new(definition.Name, definition.Description, definition.Nodes, definition.Edges, definition.SharedVariableSchema, definition.CreditBudget, definition.FailFast)
        {
            RefId = definition.WorkflowRefId
        };
}
