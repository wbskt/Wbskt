using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.Management.Models.Workflow;

public record WorkflowPublishRequest(Guid RefId, string Name, string? Description, WorkflowDefinition Definition);