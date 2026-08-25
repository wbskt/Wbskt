namespace Wbskt.Auth.Host.Services.Email;

/// <summary>
/// Settings for the mail this host sends, bound from <c>Auth:Email</c> alongside the SMTP settings
/// in <see cref="Wbskt.Infrastructure.Email.EmailOptions"/>.
/// </summary>
public sealed class AuthEmailOptions
{
    /// <summary>
    /// Where the links in our mail point. Every token this host issues is redeemed through the
    /// console, not through the API directly, so the console's public origin is the only thing that
    /// can be put in front of a recipient.
    /// <para>
    /// Configured rather than derived from the request: a link built from an inbound Host header is
    /// a link an attacker can choose, which turns every password-reset mail we send into a delivery
    /// mechanism for their own domain.
    /// </para>
    /// </summary>
    public string ConsoleBaseUrl { get; init; } = "http://localhost:5173";

    /// <summary>Shown as the sender's name and in the mail body. Purely cosmetic.</summary>
    public string ProductName { get; init; } = "Wbskt";

    /// <summary>
    /// Whether sign-in requires a confirmed address. <b>True, and it should stay true anywhere real.</b>
    /// <para>
    /// It exists because the end-to-end suite drives registration and sign-in against locally run
    /// hosts with no relay and no inbox to read a link out of, and without an escape the whole suite
    /// is unrunnable. It is set false in <c>appsettings.Development.json</c> and nowhere else, so a
    /// deployed image — which runs as Production — keeps the check whatever the config file says.
    /// </para>
    /// <para>
    /// Turning it off does not stop verification mail being sent or tokens being issued; it only
    /// stops the check at the door. The host logs an error at startup and a warning on every
    /// unverified sign-in while it is off, because a setting like this is only safe when it is loud.
    /// </para>
    /// </summary>
    public bool RequireVerifiedEmailForSignIn { get; init; } = true;
}
