using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Management.Host.Services.Clients;

namespace Wbskt.Management.Host.Authorization;

/// <summary>
/// Enforces <see cref="RequiresPermissionAttribute"/> on the management host. The workspace is
/// resolved once per request through <see cref="IAuthServiceClient"/>, which caches it, whatever
/// the number of attributes.
/// </summary>
public sealed class WorkspacePermissionFilter : ScopedPermissionFilter<RequiresPermissionAttribute>
{
    public const string WorkspaceRefRouteKey = "workspaceRef";

    protected override string RouteKey => WorkspaceRefRouteKey;

    protected override async Task<Result<PermissionScope>> ResolveAsync(HttpContext httpContext, Guid scopeRef, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        var authClient = httpContext.RequestServices.GetRequiredService<IAuthServiceClient>();
        var access = await authClient.ResolveWorkspaceAsync(scopeRef, cancellationToken);
        return access.IsFailure
            ? Result<PermissionScope>.Failure(access.Error)
            : Result<PermissionScope>.Success(new PermissionScope(access.Value.WorkspaceId, access.Value.Permissions.ToHashSet(StringComparer.Ordinal)));
    }

    /// <summary>The workspace this request was authorized for, once the filter has passed it.</summary>
    public static bool TryGetWorkspaceId(HttpContext httpContext, out int workspaceId) => TryGetScopeId(httpContext, out workspaceId);
}
