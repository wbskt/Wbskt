using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Wbskt.Infrastructure.Authorization;

/// <summary>
/// A resolved scope: its internal id, which of the permissions asked about the caller holds in it, and
/// whether its account is frozen by Wbskt support, which allows reads only.
/// </summary>
public sealed record PermissionScope(int Id, IReadOnlySet<string> Granted, bool Frozen = false)
{
    /// <summary>The scope a resolver answers with: the permission names compared ordinally.</summary>
    public static PermissionScope Of(int id, IEnumerable<string> granted, bool frozen = false) => new(id, granted.ToHashSet(StringComparer.Ordinal), frozen);
}

/// <summary>
/// Enforces <typeparamref name="TAttribute"/> on the actions that carry it. Runs as an authorization
/// filter, so before model binding: the scope id can then be bound, and a refused request never
/// reaches the action or its validation. A host derives one per scope it has (workspace, tenant)
/// and says how that scope is resolved.
/// </summary>
/// <remarks>
/// A refusal is the resolve's own error as is (403 for a scope the caller cannot use), 409
/// <c>ACCOUNT_FROZEN</c> for an action needing anything but a read permission (<c>*.read</c>) in a frozen
/// scope, or 403 <c>PERMISSION_UNAUTHORIZED</c> naming what is missing.
/// </remarks>
public abstract class ScopedPermissionFilter<TAttribute> : IAsyncAuthorizationFilter where TAttribute : PermissionRequirementAttribute
{
    private static readonly object ScopeIdKey = new();
    private const string ReadSuffix = ".read";

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

        // A frozen account keeps its reads; the freeze is checked before permissions so the answer says why.
        if (scope.Value.Frozen && !requirements.All(IsRead))
        {
            context.Result = ErrorResult(context, Error.Conflict("ACCOUNT_FROZEN", "Wbskt support has frozen this account. It can be read, but nothing in it can be changed."));
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

    /// <summary>Whether a requirement can be met with reads alone, which is all a frozen scope allows.</summary>
    private static bool IsRead(TAttribute requirement) => requirement.Mode == PermissionMatch.Any
        ? requirement.Permissions.Any(IsRead)
        : requirement.Permissions.All(IsRead);

    private static bool IsRead(string permission) => permission.EndsWith(ReadSuffix, StringComparison.Ordinal);

    private IActionResult ErrorResult(AuthorizationFilterContext context, Error error)
    {
        // The controller is not built yet, so map the way ApiControllerBase does.
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());
        return ApiErrorResults.From(error, logger);
    }
}
