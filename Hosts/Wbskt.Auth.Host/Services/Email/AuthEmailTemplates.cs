using System.Net;
using System.Text;

namespace Wbskt.Auth.Host.Services.Email;

/// <summary>
/// Renders the four messages this host sends. Hand-written rather than backed by a template engine:
/// four messages with one shared frame does not justify a dependency, and keeping the markup in C#
/// means the compiler sees every field a template references.
/// </summary>
/// <remarks>
/// <b>Every interpolated value is HTML-encoded.</b> Usernames, tenant names and email addresses are
/// chosen by users, and these bodies are the one place in the platform where user-supplied text is
/// rendered as markup in someone else's client. <see cref="Field"/> exists so that encoding is the
/// default rather than something each template has to remember.
/// </remarks>
internal static class AuthEmailTemplates
{
    public static OutboundMail Invitation(AuthEmailOptions options, string to, string tenantName, string token, DateTime expiresAtUtc)
    {
        string url = Link(options, "accept-invitation", token);

        return new OutboundMail(
            to,
            Subject($"You have been invited to {tenantName} on {options.ProductName}"),
            Layout(options,
                $"You have been invited to join {Field(tenantName)}",
                $"""
                 <p>Someone has invited you to join the {Field(tenantName)} workspace on {Field(options.ProductName)}.</p>
                 {Button(url, "Accept the invitation")}
                 {Expiry(expiresAtUtc)}
                 <p class="muted">If you were not expecting this, you can ignore this message. Nothing happens until you accept.</p>
                 """));
    }

    public static OutboundMail EmailVerification(AuthEmailOptions options, string to, string username, string token, DateTime expiresAtUtc)
    {
        string url = Link(options, "verify-email", token);

        return new OutboundMail(
            to,
            Subject($"Confirm your email address for {options.ProductName}"),
            Layout(options,
                "Confirm your email address",
                $"""
                 <p>Hello {Field(username)} — confirm this address to finish setting up your {Field(options.ProductName)} account. You will not be able to sign in until you do.</p>
                 {Button(url, "Confirm this address")}
                 {Expiry(expiresAtUtc)}
                 <p class="muted">If you did not create this account, someone entered your address by mistake. Ignoring this message leaves the account unusable.</p>
                 """));
    }

    public static OutboundMail PasswordReset(AuthEmailOptions options, string to, string username, string token, DateTime expiresAtUtc)
    {
        string url = Link(options, "reset-password", token);

        return new OutboundMail(
            to,
            Subject($"Reset your {options.ProductName} password"),
            Layout(options,
                "Reset your password",
                $"""
                 <p>Hello {Field(username)} — someone asked to reset the password for your {Field(options.ProductName)} account.</p>
                 {Button(url, "Choose a new password")}
                 {Expiry(expiresAtUtc)}
                 <p class="muted">If that was not you, no action is needed and your password has not changed. Signing in everywhere else stays as it was until this link is used.</p>
                 """));
    }

    /// <summary>
    /// Sent when someone tries to register an address that already has an account. Registration
    /// answers identically either way, so this message is the only thing that distinguishes the two
    /// cases — and it goes to the address's real owner, not to whoever submitted the form.
    /// </summary>
    public static OutboundMail AccountAlreadyExists(AuthEmailOptions options, string to, string username)
    {
        string url = $"{options.ConsoleBaseUrl.TrimEnd('/')}/forgot-password";

        return new OutboundMail(
            to,
            Subject($"Someone tried to register your {options.ProductName} account"),
            Layout(options,
                "You already have an account",
                $"""
                 <p>Hello {Field(username)} — someone just tried to create a {Field(options.ProductName)} account with this address, but you already have one.</p>
                 <p>If that was you, sign in as usual. If you have forgotten your password, you can reset it.</p>
                 {Button(url, "Reset your password")}
                 <p class="muted">No account was created and nothing about yours has changed. If this was not you, no action is needed.</p>
                 """));
    }

    /// <summary>
    /// HTML-encodes a user-supplied value. Every interpolation in a template body goes through this.
    /// </summary>
    private static string Field(string value) => WebUtility.HtmlEncode(value);

    /// <summary>
    /// Flattens a composed subject line to a single line. A subject is a mail <em>header</em>, not
    /// markup, so HTML encoding is the wrong tool and would only make it unreadable — the risk here
    /// is a tenant name or product name carrying a line break and continuing into a header of the
    /// sender's choosing. Every control character goes, not just CR and LF, because which ones a
    /// given relay treats as a terminator is not worth reasoning about one at a time.
    /// </summary>
    private static string Subject(string value)
    {
        var sb = new StringBuilder(value.Length);
        bool lastWasSpace = false;

        foreach (char c in value)
        {
            bool isBreak = char.IsControl(c) || c == '\u2028' || c == '\u2029';
            if (isBreak || c == ' ')
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            sb.Append(c);
            lastWasSpace = false;
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Tokens from <see cref="SecurityTokens.Generate"/> are already URL-safe base64, so this escape
    /// changes nothing today. It is here so the link stays correct if the generator ever does.
    /// </summary>
    private static string Link(AuthEmailOptions options, string path, string token) =>
        $"{options.ConsoleBaseUrl.TrimEnd('/')}/{path}?token={Uri.EscapeDataString(token)}";

    private static string Expiry(DateTime expiresAtUtc) =>
        $"""<p class="muted">This link stops working at {expiresAtUtc:yyyy-MM-dd HH:mm} UTC, and can only be used once.</p>""";

    private static string Button(string url, string label)
    {
        // The URL is built by Link() from configured origins and a generated token, so it carries no
        // user input - but it is still attribute-encoded, because a href is the one place where
        // getting that wrong is worst.
        string href = WebUtility.HtmlEncode(url);
        return $"""
                <p><a class="button" href="{href}">{Field(label)}</a></p>
                <p class="muted">Or paste this into your browser:<br><span class="url">{href}</span></p>
                """;
    }

    private static string Layout(AuthEmailOptions options, string heading, string body)
    {
        var sb = new StringBuilder();

        // Inline styles and a table-free single column: mail clients strip <style> blocks
        // inconsistently, and anything more elaborate renders differently in every one of them.
        sb.Append("""
                  <!doctype html><html><head><meta charset="utf-8">
                  <style>
                    body { font-family: -apple-system, Segoe UI, Roboto, Helvetica, Arial, sans-serif; font-size: 15px; line-height: 1.55; color: #14191d; background: #f5f7f8; margin: 0; padding: 24px; }
                    .card { max-width: 520px; margin: 0 auto; background: #ffffff; border: 1px solid #dbe3e7; border-radius: 6px; padding: 28px 32px; }
                    h1 { font-size: 19px; margin: 0 0 18px; }
                    p { margin: 0 0 14px; }
                    .button { display: inline-block; background: #0d6b7a; color: #ffffff !important; text-decoration: none; padding: 10px 18px; border-radius: 4px; font-weight: 600; }
                    .muted { color: #56626c; font-size: 13px; }
                    .url { word-break: break-all; font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; font-size: 12px; color: #56626c; }
                    .footer { color: #8794a0; font-size: 12px; margin-top: 22px; }
                  </style></head><body><div class="card">
                  """);

        sb.Append("<h1>").Append(Field(heading)).Append("</h1>");
        sb.Append(body);
        sb.Append("""<p class="footer">""").Append(Field(options.ProductName)).Append("</p>");
        sb.Append("</div></body></html>");

        return sb.ToString();
    }
}
