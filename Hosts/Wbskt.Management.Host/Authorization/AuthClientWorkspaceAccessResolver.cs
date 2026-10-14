using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Management.Host.Services.Clients;

namespace Wbskt.Management.Host.Authorization;

/// <summary>
/// Resolves workspace routes through <see cref="IAuthServiceClient"/>, which asks the auth host and
/// caches its answer.
/// </summary>
public sealed class AuthClientWorkspaceAccessResolver(IAuthServiceClient authClient) : IWorkspaceAccessResolver
{
    public async Task<Result<PermissionScope>> ResolveAsync(Guid workspaceRef, CancellationToken cancellationToken)
    {
        var access = await authClient.ResolveWorkspaceAsync(workspaceRef, cancellationToken);
        return access.IsFailure
            ? Result<PermissionScope>.Failure(access.Error)
            : Result<PermissionScope>.Success(new PermissionScope(access.Value.WorkspaceId, access.Value.Permissions.ToHashSet(StringComparer.Ordinal)));
    }
}
