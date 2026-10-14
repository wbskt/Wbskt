using Wbskt.Auth.Host.Providers;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Auth.Host.Services;

/// <summary>The auth host owns users, so it reads the id from its own database.</summary>
internal sealed class AuthUserIdLookup(IAccountProvider provider) : IUserIdLookup
{
    public async Task<int?> FindUserIdAsync(Guid userRef, CancellationToken cancellationToken)
    {
        int userId = await provider.FindIdByRefIdAsync(userRef, cancellationToken);
        return userId > 0 ? userId : null;
    }
}
