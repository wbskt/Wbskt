using System.Security;

namespace Wbskt.Infrastructure.Security;

public class IdentityService : IIdentityService
{
    private static readonly AsyncLocal<UserIdentity?> CurrentIdentity = new();

    public UserIdentity GetUserIdentity()
    {
        return CurrentIdentity.Value ?? throw new SecurityException($"{nameof(IdentityService)}.{nameof(GetUserIdentity)}() returned null");
    }

    public IDisposable BeginScope(UserIdentity scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var previous = CurrentIdentity.Value;
        CurrentIdentity.Value = scope;

        return new IdentityScope(() => CurrentIdentity.Value = previous);
    }

    private sealed class IdentityScope(Action reset) : IDisposable
    {
        public void Dispose() => reset();
    }
}
