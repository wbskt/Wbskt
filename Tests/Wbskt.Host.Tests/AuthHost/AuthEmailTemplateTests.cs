using Wbskt.Auth.Host.Services.Email;

namespace Wbskt.Host.Tests.AuthHost;

/// <summary>
/// These bodies are the one place in the platform where user-chosen text is rendered as markup in
/// someone else's mail client. Usernames and tenant names are user-chosen.
/// </summary>
public sealed class AuthEmailTemplateTests
{
    private static readonly AuthEmailOptions Options = new()
    {
        ConsoleBaseUrl = "https://console.example.test/",
        ProductName = "Wbskt"
    };

    private static readonly DateTime Expiry = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_username_containing_markup_is_encoded_not_rendered()
    {
        var mail = AuthEmailTemplates.PasswordReset(Options, "victim@example.test", "<script>alert(1)</script>", "tok", Expiry);

        Assert.DoesNotContain("<script>", mail.HtmlBody);
        Assert.Contains("&lt;script&gt;", mail.HtmlBody);
    }

    [Fact]
    public void A_tenant_name_containing_markup_is_encoded_not_rendered()
    {
        var mail = AuthEmailTemplates.Invitation(Options, "invitee@example.test", "<img src=x onerror=alert(1)>", "tok", Expiry);

        // "<img src=x", not "<img": the layout's own logo is an image.
        Assert.DoesNotContain("<img src=x", mail.HtmlBody);
        Assert.Contains("&lt;img src=x", mail.HtmlBody);
    }

    /// <summary>
    /// A tenant name is attacker-chosen and lands in the subject line as well as the body. Subject
    /// lines are not markup, so they are not encoded — but they must not be able to inject a header,
    /// which is what a newline would do.
    /// </summary>
    /// <summary>
    /// Encoded once, not twice. The heading is the only place a template interpolates a user value
    /// into text that <c>Layout</c> then encodes itself, so it is the one place double-encoding can
    /// happen — and it renders as a literal "&amp;amp;" in the recipient's client rather than
    /// failing anywhere a developer would see.
    /// </summary>
    [Fact]
    public void A_name_in_the_heading_is_encoded_exactly_once()
    {
        var mail = AuthEmailTemplates.Invitation(Options, "invitee@example.test", "Smith & Sons", "tok", Expiry);

        Assert.Contains(">You have been invited to join Smith &amp; Sons</h1>", mail.HtmlBody);
        Assert.DoesNotContain("&amp;amp;", mail.HtmlBody);
    }

    [Fact]
    public void A_subject_line_carries_no_line_breaks()
    {
        var mail = AuthEmailTemplates.Invitation(Options, "invitee@example.test", "Acme\r\nBcc: everyone@example.test", "tok", Expiry);

        Assert.DoesNotContain('\r', mail.Subject);
        Assert.DoesNotContain('\n', mail.Subject);
    }

    [Fact]
    public void The_link_points_at_the_configured_console_and_carries_the_token()
    {
        var mail = AuthEmailTemplates.EmailVerification(Options, "someone@example.test", "someone", "abc-123_XYZ", Expiry);

        Assert.Contains("https://console.example.test/verify-email?token=abc-123_XYZ", mail.HtmlBody);

        // TrimEnd on the configured base, so a trailing slash does not become a double one.
        Assert.DoesNotContain("example.test//", mail.HtmlBody);
    }

    /// <summary>
    /// The four messages must not be interchangeable: a reset link delivered under a verification
    /// subject is a phishing lesson we would be teaching our own users.
    /// </summary>
    [Fact]
    public void Each_message_addresses_its_own_path()
    {
        Assert.Contains("reset-password", AuthEmailTemplates.PasswordReset(Options, "a@example.test", "a", "t", Expiry).HtmlBody);
        Assert.Contains("verify-email", AuthEmailTemplates.EmailVerification(Options, "a@example.test", "a", "t", Expiry).HtmlBody);
        Assert.Contains("accept-invitation", AuthEmailTemplates.Invitation(Options, "a@example.test", "Acme", "t", Expiry).HtmlBody);

        // The "you already have an account" mail carries no token at all - it is sent to an address
        // whose owner did not ask for anything, so it must not contain a credential.
        var exists = AuthEmailTemplates.AccountAlreadyExists(Options, "a@example.test", "a");
        Assert.Contains("forgot-password", exists.HtmlBody);
        Assert.DoesNotContain("token=", exists.HtmlBody);
    }
    /// <summary>
    /// The plain-text body is what text-only clients and anyone blocking HTML read, so it carries the
    /// same link, and user-chosen values verbatim: nothing renders it, so encoding would only garble it.
    /// </summary>
    [Fact]
    public void The_text_body_carries_the_link_and_no_markup()
    {
        var mail = AuthEmailTemplates.Invitation(Options, "invitee@example.test", "Smith & <Sons>", "abc", Expiry);

        Assert.Contains("https://console.example.test/accept-invitation?token=abc", mail.TextBody);
        Assert.Contains("Smith & <Sons>", mail.TextBody);
        Assert.DoesNotContain("&amp;", mail.TextBody);
        Assert.DoesNotContain("<p", mail.TextBody);
    }

    /// <summary>The preheader is the inbox preview line and is user-influenced, so it is encoded like the rest.</summary>
    [Fact]
    public void The_preheader_is_encoded()
    {
        var mail = AuthEmailTemplates.Invitation(Options, "invitee@example.test", "<b>Acme</b>", "tok", Expiry);

        Assert.Contains("Join the &lt;b&gt;Acme&lt;/b&gt; workspace", mail.HtmlBody);
        Assert.DoesNotContain("<b>Acme", mail.HtmlBody);
    }

    [Fact]
    public void The_layout_carries_the_configured_logo_and_footer_links()
    {
        var options = new AuthEmailOptions { ConsoleBaseUrl = "https://console.example.test", WebsiteUrl = "https://site.example.test/", LogoUrl = "https://site.example.test/mark.png" };

        var mail = AuthEmailTemplates.PasswordReset(options, "a@example.test", "a", "t", Expiry);

        Assert.Contains("src=\"https://site.example.test/mark.png\"", mail.HtmlBody);
        Assert.Contains("href=\"https://site.example.test/privacy\"", mail.HtmlBody);
        Assert.Contains("https://site.example.test/terms", mail.TextBody);
    }
}
