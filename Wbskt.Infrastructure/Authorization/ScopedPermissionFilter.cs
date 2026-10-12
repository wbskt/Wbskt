using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Wbskt.Infrastructure.Authorization;

/// <summary>A resolved scope: its internal id, and which of the permissions asked about the caller holds in it.</summary>
public sealed record PermissionScope(int Id, IReadOnlySet<string> Granted);

/// <summary>
/// Enforces <typeparamref name="TAttribute"/> on the actions that carry it. Runs as an authorization
/// filter, so before model binding: the scope id can then be bound, and a refused request never
/// reaches the action or its validation. A host derives one per scope it has (workspace, tenant)
/// and says how that scope is resolved.
/// </summary>
/// <remarks>
/// A refusal is the resolve's own error as is (403 for a scope the caller cannot use), or 403
/// <c>PERMISSION_UNAUTHORIZED</c> naming what is missing.
/// </remarks>
public abstract class ScopedPermissionFilter<TAttribute> : IAsyncAuthorizationFilter where TAttribute : PermissionRequirementAttribute
{
    private static readonly object ScopeIdKey = new();

    /// <summary>The route value holding the scope's reference, such as <c>workspaceRef</c>.</summary>
    protected abstract string RouteKey { get; }

    /// <summary>
    /// Resolves the scope named by <paramref name="scopeRef"/> for the caller, and which of
    /// <paramref name="permissions"/> they hold in it; or the error that refuses the request.
    /// </summary>
    protected abstract Task<Result<PermissionScope>> ResolveAsync(HttpContext httpContext, Guid scopeRef, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken);

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor action)
        {
            return;
        }

        var requirements = RequirementsFor(action.MethodInfo, action.ControllerTypeInfo);
        if (requirements.Count == 0)
        {
            return;
        }

        if (!Guid.TryParse(context.RouteData.Values[RouteKey]?.ToString(), out var scopeRef))
        {
            throw new InvalidOperationException(
                $"{action.DisplayName} carries [{typeof(TAttribute).Name}] but its route has no {{{RouteKey}:guid}}.");
        }

        var named = requirements.SelectMany(r => r.Permissions).Distinct(StringComparer.Ordinal).ToList();
        var scope = await ResolveAsync(context.HttpContext, scopeRef, named, context.HttpContext.RequestAborted);
        if (scope.IsFailure)
        {
            context.Result = ErrorResult(context, scope.Error);
            return;
        }

        foreach (var requirement in requirements)
        {
            var missing = requirement.Missing(scope.Value.Granted.Contains);
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

        context.HttpContext.Items[ScopeIdKey] = scope.Value.Id;
    }

    /// <summary>The scope this request was authorized for, once the filter has passed it.</summary>
    public static bool TryGetScopeId(HttpContext httpContext, out int scopeId)
    {
        if (httpContext.Items.TryGetValue(ScopeIdKey, out var value) && value is int id)
        {
            scopeId = id;
            return true;
        }

        scopeId = 0;
        return false;
    }

    /// <summary>
    /// The requirements that apply to an action: its own attributes if it has any, otherwise its
    /// controller's (including inherited ones).
    /// </summary>
    public static IReadOnlyList<TAttribute> RequirementsFor(MethodInfo method, Type controller)
    {
        var own = method.GetCustomAttributes<TAttribute>(inherit: true).ToList();
        return own.Count > 0 ? own : controller.GetCustomAttributes<TAttribute>(inherit: true).ToList();
    }

    private IActionResult ErrorResult(AuthorizationFilterContext context, Error error)
    {
        // The controller is not built yet, so map the way ApiControllerBase does.
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());
        return ApiErrorResults.From(error, logger);
    }
}
