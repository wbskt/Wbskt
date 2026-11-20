using Microsoft.Data.SqlClient;
using Wbskt.Infrastructure;

namespace Wbskt.Management.Host.Services.Workflow;

/// <summary>
/// The refusals the workflow lifecycle procedures THROW, as the errors the API answers with. The
/// procedures decide which state changes are legal, under the workflow's row lock; this is the one place
/// their numbers are read, so there is no second copy of the rules to drift from them.
/// </summary>
internal static class WorkflowLifecycleErrors
{
    public const int OwnedElsewhere = 50021;
    public const int Deleted = 50022;
    public const int IntegrationMissing = 50023;
    public const int LimitReached = 50024;
    public const int VersionMoved = 50025;
    public const int NotFound = 50040;
    public const int IsDeprecated = 50041;
    public const int NotDeprecated = 50042;
    public const int NoSuchVersion = 50043;

    public static readonly Error WorkflowDeprecated = Error.Conflict(
        "WORKFLOW_DEPRECATED",
        "The workflow is deprecated, so it cannot be changed or enabled. Reinstate it first.");

    public static readonly Error WorkflowNotDeprecated = Error.Conflict("WORKFLOW_NOT_DEPRECATED", "The workflow is not deprecated.");

    public const int MaxNoteLength = 500;

    public static readonly Error NoteTooLong = Error.Validation(
        "WORKFLOW_VALIDATION_FAILED",
        $"[NOTE_TOO_LONG] A version's note can be at most {MaxNoteLength} characters.");

    public static readonly Error WorkflowDeleted = Error.Conflict("WORKFLOW_DELETED", "The workflow was deleted and cannot be published again.");

    public static readonly Error IntegrationDeleted = Error.Validation(
        "WORKFLOW_VALIDATION_FAILED",
        "[INTEGRATION_NOT_FOUND] An integration a step sends through was just deleted. Choose another and publish again.");

    public static Error VersionNotFound(Guid refId, int version) =>
        Error.NotFound("WORKFLOW_VERSION_NOT_FOUND", $"Workflow '{refId}' has no version {version}.");

    /// <summary>
    /// The error for a refusal any lifecycle procedure may throw. False for a number that needs its
    /// caller's context (the workflow limit, a moved version, a RefId taken elsewhere), and for a fault.
    /// </summary>
    public static bool TryMap(SqlException ex, Guid refId, int? version, out Error error)
    {
        Error? mapped = ex.Number switch
        {
            NotFound => WorkspaceOwnership.WorkflowNotFound,
            IsDeprecated => WorkflowDeprecated,
            NotDeprecated => WorkflowNotDeprecated,
            NoSuchVersion => VersionNotFound(refId, version ?? 0),
            IntegrationMissing => IntegrationDeleted,
            Deleted => WorkflowDeleted,
            _ => null
        };

        error = mapped ?? Error.Failure("WORKFLOW_LIFECYCLE_FAILED", ex.Message);
        return mapped is not null;
    }
}
