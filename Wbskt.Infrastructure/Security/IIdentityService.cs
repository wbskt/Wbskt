using System.Diagnostics.CodeAnalysis;

namespace Wbskt.Infrastructure.Security;

public interface IIdentityService
{
    UserIdentity GetUserIdentity();

    /// <summary>
    /// Non-throwing counterpart to <see cref="GetUserIdentity"/>, for callers that treat an
    /// unauthenticated request as an expected outcome rather than an exceptional one.
    /// </summary>
    bool TryGetUserIdentity([NotNullWhen(true)] out UserIdentity? identity);

    IDisposable BeginScope(UserIdentity scope);
}
