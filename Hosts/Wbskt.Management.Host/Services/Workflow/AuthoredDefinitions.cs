using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.SharedVariables;

namespace Wbskt.Management.Host.Services.Workflow;

/// <summary>
/// Builds the definition a publish, a validation or a draft stores from what the author sent: the
/// workflow's id is the request's, and when it was made is the server's, whatever the body says. The
/// version and the publisher are set when it is stored. Null when the body holds no steps at all, which
/// the validator reports.
/// </summary>
internal static class AuthoredDefinitions
{
    public static WorkflowDefinition? From(WorkflowPublishRequest request, DateTime now) =>
        Build(request.RefId, request.Name, request.Description, request.Nodes, request.Edges, request.SharedVariableSchema, request.CreditBudget, request.FailFast, request.Definition, now);

    public static WorkflowDefinition? From(WorkflowValidationRequest request, DateTime now) =>
        Build(request.RefId, request.Name, request.Description, request.Nodes, request.Edges, request.SharedVariableSchema, request.CreditBudget, request.FailFast, request.Definition, now);

    public static WorkflowDefinition? From(Guid refId, WorkflowDraftSaveRequest request, DateTime now) =>
        Build(refId, request.Name, request.Description, request.Nodes, request.Edges, request.SharedVariableSchema, request.CreditBudget, request.FailFast, request.Definition, now);

    private static WorkflowDefinition? Build(
        Guid? refId,
        string? name,
        string? description,
        IReadOnlyCollection<BaseNode>? nodes,
        IReadOnlyCollection<Edge>? edges,
        IReadOnlyCollection<SharedVariableDeclaration>? sharedVariables,
        decimal? creditBudget,
        bool failFast,
        WorkflowDefinition? nested,
        DateTime now)
    {
        if (nodes is null)
        {
            // The earlier nested body, kept for one release (#1059). Its own copies of the id, name and
            // description give way to the request's, and its time to the server's.
            return nested is null
                ? null
                : nested with
                {
                    WorkflowRefId = refId ?? nested.WorkflowRefId,
                    Name = name ?? nested.Name,
                    Description = name is null ? nested.Description : description,
                    CreatedAt = now
                };
        }

        return new WorkflowDefinition(
            refId ?? Guid.Empty,
            // WorkflowDefinition_Publish stamps the real one into the stored JSON; this only satisfies the
            // validator, which reads a definition as stored.
            Version: 1,
            WorkspaceId: 0,
            name ?? string.Empty,
            description,
            nodes,
            edges ?? [],
            sharedVariables ?? [],
            now,
            PublishedBy: 0,
            FailFast: failFast,
            CreditBudget: creditBudget);
    }
}
