namespace Wbskt.Infrastructure;

public static class RateLimitPolicies
{
    /// <summary>
    /// Applied to the unauthenticated credential endpoints — login, register, refresh, logout.
    /// These are reachable from the internet and each one costs a password hash or a database
    /// write, so they are the natural target for credential stuffing and sign-up spam.
    /// </summary>
    public const string Authentication = "authentication";

    /// <summary>
    /// The refresh-token exchange. Separate from <see cref="Authentication"/> because short-lived
    /// access tokens make refreshing routine traffic rather than a credential attempt.
    /// </summary>
    public const string TokenRefresh = "token-refresh";
}
