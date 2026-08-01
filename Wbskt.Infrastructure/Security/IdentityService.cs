using System.Diagnostics.CodeAnalysis;
using System.Security;

namespace Wbskt.Infrastructure.Security;

public class IdentityService : IIdentityService
{
    private static readonly AsyncLocal<UserIdentity?> CurrentIdentity = new();

    // [RJ]: TODO: remove usages of this and switch to the TryGetUserIdentity
    public UserIdentity GetUserIdentity()
    {
        return CurrentIdentity.Value ?? throw new SecurityException($"{nameof(IdentityService)}.{nameof(GetUserIdentity)}() returned null");
    }

    public bool TryGetUserIdentity([NotNullWhen(true)] out UserIdentity? identity)
    {
        identity = CurrentIdentity.Value;
        return identity is not null;
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
