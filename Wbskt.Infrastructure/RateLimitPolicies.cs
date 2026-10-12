namespace Wbskt.Infrastructure;

public static class RateLimitPolicies
{
    /// <summary>
    /// Applied to the credential endpoints — login, register, change-password, forgot-password and
    /// reset-password. These are reachable from the internet and each one costs a password hash or a
    /// database write, so they are the natural target for credential stuffing and sign-up spam.
    /// </summary>
    public const string Authentication = "authentication";

    /// <summary>
    /// The refresh-token exchange and logout. Separate from <see cref="Authentication"/> because
    /// short-lived access tokens make refreshing routine traffic rather than a credential attempt,
    /// and signing out must not spend the budget a colleague on the same address needs to sign in.
    /// </summary>
    public const string TokenRefresh = "token-refresh";

    /// <summary>
    /// verify-email and resend-verification. Separate from <see cref="Authentication"/> so following
    /// a verification link cannot lock anyone out of signing in.
    /// </summary>
    public const string EmailVerification = "email-verification";

    /// <summary>
    /// Device enrollment by registration PIN. Anonymous by necessity - the device has no credential
    /// yet - and the PIN is the only thing standing between the internet and a device in someone's
    /// workspace, so guessing at it has to be slow. Partitioned per IP.
    /// </summary>
    public const string DeviceRegistration = "device-registration";

    /// <summary>
    /// Device login with its client secret. Anonymous like enrollment, but a secret is far longer
    /// than a PIN and a whole fleet behind one NAT logs in after every restart, so the budget is
    /// generous: it stops a runaway loop or a flood, not a fleet. Partitioned per IP.
    /// </summary>
    public const string DeviceLogin = "device-login";

    /// <summary>
    /// Testing an integration. Each test reaches out to someone else's service (a bot API, a mail
    /// relay) with a workspace's credential, so a runaway console or script must not turn the engine
    /// into a flood against it. Partitioned per workspace.
    /// </summary>
    public const string IntegrationTest = "integration-test";
}
