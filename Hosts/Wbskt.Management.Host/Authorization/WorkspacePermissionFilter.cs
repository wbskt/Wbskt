using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Services.Clients;

namespace Wbskt.Management.Host.Authorization;

/// <summary>
/// Enforces <see cref="RequiresPermissionAttribute"/>. Runs as an authorization filter, so before
/// model binding: <see cref="FromWorkspaceAttribute"/> can then bind the ID it resolved, and a
/// refused request never reaches the action or its validation.
/// </summary>
/// <remarks>
/// The workspace is resolved once per request through <see cref="IAuthServiceClient"/>, which caches
/// it, whatever the number of attributes. A refusal is answered exactly as the hand-written checks
/// answered it: the resolve error as is (403 for a workspace the caller cannot use), or 403
/// <c>PERMISSION_UNAUTHORIZED</c> naming what is missing.
/// </remarks>
public sealed class WorkspacePermissionFilter : IAsyncAuthorizationFilter
{
    public const string WorkspaceRefRouteKey = "workspaceRef";

    private static readonly object WorkspaceIdKey = new();

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor action)
        {
            return;
        }

        var requirements = RequirementsFor(action);
        if (requirements.Count == 0)
        {
            return;
        }

        if (!Guid.TryParse(context.RouteData.Values[WorkspaceRefRouteKey]?.ToString(), out var workspaceRef))
        {
            throw new InvalidOperationException(
                $"{action.DisplayName} carries [RequiresPermission] but its route has no {{{WorkspaceRefRouteKey}:guid}}.");
        }

        var authClient = context.HttpContext.RequestServices.GetRequiredService<IAuthServiceClient>();
        var accessResult = await authClient.ResolveWorkspaceAsync(workspaceRef, context.HttpContext.RequestAborted);
        if (accessResult.IsFailure)
        {
            context.Result = ErrorResult(context, accessResult.Error);
            return;
        }

        var access = accessResult.Value;
        foreach (var requirement in requirements)
        {
            var missing = requirement.Missing(p => access.Permissions.Contains(p));
            if (missing.Count == 0)
            {
                continue;
            }

            var needed = requirement.Mode == PermissionMatch.Any
                ? $"one of {string.Join(", ", requirement.Permissions)}"
                : string.Join(", ", missing);
            context.Result = ErrorResult(context, Error.Forbidden("PERMISSION_UNAUTHORIZED", $"user does not have permission(s) {needed}"));
            return;
        }

        context.HttpContext.Items[WorkspaceIdKey] = access.WorkspaceId;
    }

    /// <summary>The workspace this request was authorized for, once the filter has passed it.</summary>
    public static bool TryGetWorkspaceId(HttpContext httpContext, out int workspaceId)
    {
        if (httpContext.Items.TryGetValue(WorkspaceIdKey, out var value) && value is int id)
        {
            workspaceId = id;
            return true;
        }

        workspaceId = 0;
        return false;
    }

    /// <summary>
    /// The requirements that apply to an action: its own attributes if it has any, otherwise its
    /// controller's (including inherited ones).
    /// </summary>
    public static IReadOnlyList<RequiresPermissionAttribute> RequirementsFor(ControllerActionDescriptor action)
    {
        return RequirementsFor(action.MethodInfo, action.ControllerTypeInfo);
    }

    public static IReadOnlyList<RequiresPermissionAttribute> RequirementsFor(MethodInfo method, Type controller)
    {
        var own = method.GetCustomAttributes<RequiresPermissionAttribute>(inherit: true).ToList();
        return own.Count > 0 ? own : controller.GetCustomAttributes<RequiresPermissionAttribute>(inherit: true).ToList();
    }

    private static IActionResult ErrorResult(AuthorizationFilterContext context, Error error)
    {
        // The controller is not built yet, so map the way ApiControllerBase does.
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<WorkspacePermissionFilter>>();
        return ApiErrorResults.From(error, logger);
    }
}
