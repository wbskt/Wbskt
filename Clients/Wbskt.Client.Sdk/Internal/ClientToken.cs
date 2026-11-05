using System.Security;

namespace Wbskt.Client.Sdk.Internal;

/// <summary>A client access token and when it stops being accepted.</summary>
internal sealed record ClientToken(string Value, DateTimeOffset ExpiresAt)
{
    /// <summary>A token this close to expiry is renewed rather than offered to the socket.</summary>
    internal static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(1);

    public bool IsUsableAt(DateTimeOffset now) => ExpiresAt - now > RenewBefore;
}

/// <summary>
/// Reuses the client token across reconnects while it is valid, so a reconnect storm is mostly
/// socket handshakes and not logins. <see cref="Invalidate"/> after the socket refuses the token.
/// </summary>
internal sealed class ClientTokenCache
{
    private readonly Func<Task<ClientToken>> _login;
    private readonly TimeProvider _time;
    private ClientToken? _token;

    public ClientTokenCache(Func<Task<ClientToken>> login, TimeProvider? time = null)
    {
        _login = login;
        _time = time ?? TimeProvider.System;
    }

    public async Task<string> GetAsync()
    {
        if (_token is { } token && token.IsUsableAt(_time.GetUtcNow()))
        {
            return token.Value;
        }

        _token = await _login();
        return _token.Value;
    }

    public void Invalidate() => _token = null;
}

/// <summary>The client login answered 429; <see cref="RetryAfter"/> is the host's <c>Retry-After</c>, when it sent one.</summary>
internal sealed class ClientLoginThrottledException(TimeSpan? retryAfter)
    : Exception("Client login was rate limited.")
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>The client login answered <c>CLIENT_UNAUTHORIZED</c>: the client was deleted or its secret rotated.</summary>
internal sealed class CredentialsRejectedException()
    : SecurityException("The stored client credentials were rejected.");
