using Wbskt.Common.Configurations;

namespace Wbskt.Workflow.Api.Core.Abstractions;

// Represents the result of an action, indicating success or failure.
public record ActionResult(bool IsSuccess, string? ErrorMessage = null);

// Every action must implement this interface.
public interface IAction
{
    Task<ActionResult> ExecuteAsync(StepConfigurationBase configuration, WorkflowContext context, CancellationToken cancellationToken);
}
