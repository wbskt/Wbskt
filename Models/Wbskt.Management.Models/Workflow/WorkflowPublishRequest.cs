using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.Management.Models.Workflow;

/// <summary>
/// Publishes a new version. <c>Enable</c> is required: true makes the version live as it is published; false
/// leaves the live version (or a disabled workflow) as it is. A first publish without it is Disabled.
/// </summary>
public record WorkflowPublishRequest(Guid RefId, string Name, string? Description, WorkflowDefinition Definition, [property: JsonRequired] bool Enable);
