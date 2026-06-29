using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.SharedVariables;

namespace Wbskt.Workflow.Abstraction.Models;

public sealed record WorkflowDefinition(
    [property: JsonPropertyName("workflowRefId")] Guid WorkflowRefId,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonIgnore] int WorkspaceId, // will be 0 by default and be replaced in the service
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("isEnabled")] bool IsEnabled,
    [property: JsonPropertyName("nodes")] IReadOnlyCollection<BaseNode> Nodes,
    [property: JsonPropertyName("edges")] IReadOnlyCollection<Edge> Edges,
    [property: JsonPropertyName("sharedVariableSchema")] IReadOnlyCollection<SharedVariableDeclaration> SharedVariableSchema,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonIgnore] int PublishedBy, // will be 0 by default and be replaced in the service
    [property: JsonPropertyName("runCompensationOnFailure")] bool RunCompensationOnFailure = false,
    [property: JsonPropertyName("failFast")] bool FailFast = false
);
