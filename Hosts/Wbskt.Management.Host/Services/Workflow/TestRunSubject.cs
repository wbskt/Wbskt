using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.Management.Host.Services.Workflow;

/// <summary>What a test run copies: the draft's or live version's definition, and the version it is based on.</summary>
internal sealed record TestRunSubject(WorkflowDefinition Definition, int BaseVersion, string Name, string? Description);
