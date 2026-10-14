using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Wbskt.Infrastructure.Authorization;

/// <summary>
/// Enforces <see cref="RequiresPermissionAttribute"/> on workspace routes. The workspace is resolved
/// once per request through the host's <see cref="IWorkspaceAccessResolver"/>, whatever the number of
/// attributes.
/// </summary>
public sealed class WorkspacePermissionFilter : ScopedPermissionFilter<RequiresPermissionAttribute>
{
    public const string WorkspaceRefRouteKey = "workspaceRef";

    protected override string RouteKey => WorkspaceRefRouteKey;

    protected override Task<Result<PermissionScope>> ResolveAsync(HttpContext httpContext, Guid scopeRef, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        var resolver = httpContext.RequestServices.GetRequiredService<IWorkspaceAccessResolver>();
        return resolver.ResolveAsync(scopeRef, cancellationToken);
    }

    /// <summary>The workspace this request was authorized for, once the filter has passed it.</summary>
    public static bool TryGetWorkspaceId(HttpContext httpContext, out int workspaceId) => TryGetScopeId(httpContext, out workspaceId);
}
