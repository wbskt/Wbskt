using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.Management.Models.Workflow;

/// <summary>
/// A definition to check without publishing it. Only the definition is read, so a designer can send the
/// body it would publish; whether to make the version live is a publish-time choice and is not asked here.
/// </summary>
public sealed record WorkflowValidationRequest(WorkflowDefinition Definition);
