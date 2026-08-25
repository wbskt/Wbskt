using Wbskt.Auth.Host.Services.Email;

namespace Wbskt.Workflow.Engine.Host.Tests.AuthHost;

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

        Assert.DoesNotContain("<img", mail.HtmlBody);
        Assert.Contains("&lt;img", mail.HtmlBody);
    }

    /// <summary>
    /// A tenant name is attacker-chosen and lands in the subject line as well as the body. Subject
    /// lines are not markup, so they are not encoded — but they must not be able to inject a header,
    /// which is what a newline would do.
    /// </summary>
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
}
