using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Management.Host.Services;

/// <summary>
/// The workspace boundary for references, per "The ID Boundary" in Docs/Coding.Conventions.md. A
/// reference the workspace does not own gets exactly the answer a reference that names nothing gets:
/// a 404 with the resource's one code and message. Telling the two apart would confirm that a
/// guessed reference, or one kept from a workspace the caller has left, names something real.
/// </summary>
internal static class WorkspaceOwnership
{
    public static readonly Error ClientNotFound = Error.NotFound("CLIENT_NOT_FOUND", "Client not found.");

    public static readonly Error PolicyNotFound = Error.NotFound("POLICY_NOT_FOUND", "Registration policy not found.");

    public static readonly Error TemplateNotFound = Error.NotFound("TEMPLATE_NOT_FOUND", "Message template not found.");

    public static readonly Error WorkflowNotFound = Error.NotFound("WORKFLOW_NOT_FOUND", "Workflow not found.");

    public static readonly Error RunNotFound = Error.NotFound("RUN_NOT_FOUND", "Run not found.");

    /// <summary>
    /// Loads a resource and keeps it only when <paramref name="workspaceId"/> owns it; otherwise
    /// <paramref name="notFound"/>, whether the lookup found nothing or found another workspace's.
    /// Any other failure of the lookup is not a "not found" and is left to propagate.
    /// </summary>
    public static Task<Result<T>> LoadAsync<T>(int workspaceId, Func<Task<T>> load, Error notFound)
        where T : IWorkspaceOwned
    {
        return LoadAsync(workspaceId, load, resource => resource.WorkspaceId, notFound);
    }

    /// <inheritdoc cref="LoadAsync{T}(int, Func{Task{T}}, Error)"/>
    public static async Task<Result<T>> LoadAsync<T>(int workspaceId, Func<Task<T>> load, Func<T, int> workspaceOf, Error notFound)
    {
        T resource;
        try
        {
            resource = await load();
        }
        catch (Exception ex) when (ex is NotFoundException or KeyNotFoundException)
        {
            return Result<T>.Failure(notFound);
        }

        return Check(resource, workspaceId, workspaceOf, notFound);
    }

    /// <summary>The ownership check alone, for a resource already in hand.</summary>
    public static Result<T> Check<T>(T resource, int workspaceId, Func<T, int> workspaceOf, Error notFound)
    {
        return workspaceOf(resource) == workspaceId
            ? Result<T>.Success(resource)
            : Result<T>.Failure(notFound);
    }
}
