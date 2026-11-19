using System.Diagnostics.CodeAnalysis;

namespace Wbskt.Infrastructure.Security;

public interface IIdentityService
{
    /// <summary>
    /// The request's caller, or false when there is none; a service answers that with
    /// <see cref="IdentityService.NoCaller"/>.
    /// </summary>
    bool TryGetUserIdentity([NotNullWhen(true)] out UserIdentity? identity);

    IDisposable BeginScope(UserIdentity scope);
}
