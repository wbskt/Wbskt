using Wbskt.Auth.Host.Providers;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Auth.Host.Services;

/// <summary>The auth host owns users, so it reads the id from its own database.</summary>
internal sealed class AuthUserIdLookup(IAccountProvider provider) : IUserIdLookup
{
    public Task<int?> FindUserIdAsync(Guid userRef, CancellationToken cancellationToken) =>
        provider.FindIdByRefIdAsync(userRef, cancellationToken);
}
