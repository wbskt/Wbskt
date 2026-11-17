using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.Management.Models.Workflow;

/// <summary>
/// One version of a workflow, with the workflow's <c>State</c> and <c>LiveVersion</c>: the version is live
/// when the two versions are equal. Read through a run, the definition may be the frozen copy a test run
/// started from (<c>IsTestCopy</c>), and <c>State</c> is null for a workflow never published.
/// </summary>
public record WorkflowDefinitionDto(Guid RefId, int Version, WorkflowState? State, int? LiveVersion, string Name, string? Description, WorkflowDefinition Definition, DateTimeOffset CreatedAt, bool IsTestCopy = false);
