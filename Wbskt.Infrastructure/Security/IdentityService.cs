using System.Diagnostics.CodeAnalysis;

namespace Wbskt.Infrastructure.Security;

public class IdentityService : IIdentityService
{
    private static readonly AsyncLocal<UserIdentity?> CurrentIdentity = new();

    /// <summary>The answer for a request with no caller: a 401, so a client signs in again.</summary>
    public static readonly Error NoCaller = Error.Unauthorized("AUTH_UNAUTHORIZED", "Unauthorized access.");

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
