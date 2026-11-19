using System.Globalization;
using Wbskt.Auth.Host.Models;

namespace Wbskt.Auth.Host.Services.Email;

/// <summary>
/// The messages this host sends, written as <see cref="EmailContent"/> and rendered by
/// <see cref="AuthEmailRenderer"/> into matching HTML and plain-text bodies. Hand-written rather than
/// backed by a template engine: a handful of messages with one shared frame does not justify a
/// dependency, and keeping the content in C# means the compiler sees every field a message references.
/// </summary>
/// <remarks>
/// Every string here is plain text, user-chosen values included. The renderer encodes all of it for
/// the HTML body, so no template writes markup and none can forget to encode a username.
/// </remarks>
internal static class AuthEmailTemplates
{
    public static OutboundMail Invitation(AuthEmailOptions options, string to, string tenantName, string token, DateTime expiresAtUtc)
    {
        return AuthEmailRenderer.Render(options, to, new EmailContent(
            $"You have been invited to {tenantName} on {options.ProductName}",
            $"Join the {tenantName} workspace on {options.ProductName}.",
            $"You have been invited to join {tenantName}",
            [
                new EmailParagraph($"Someone has invited you to join the {tenantName} workspace on {options.ProductName}."),
                new EmailAction(Link(options, "accept-invitation", token), "Accept the invitation"),
                Expiry(expiresAtUtc),
                new EmailNote("If you were not expecting this, you can ignore this message. Nothing happens until you accept.")
            ]));
    }

    public static OutboundMail EmailVerification(AuthEmailOptions options, string to, string username, string token, DateTime expiresAtUtc)
    {
        return AuthEmailRenderer.Render(options, to, new EmailContent(
            $"Confirm your email address for {options.ProductName}",
            $"One step left: confirm this address to start using {options.ProductName}.",
            "Confirm your email address",
            [
                new EmailParagraph($"Hello {username} — confirm this address to finish setting up your {options.ProductName} account. You will not be able to sign in until you do."),
                new EmailAction(Link(options, "verify-email", token), "Confirm this address"),
                Expiry(expiresAtUtc),
                new EmailNote("If you did not create this account, someone entered your address by mistake. Ignoring this message leaves the account unusable.")
            ]));
    }

    public static OutboundMail PasswordReset(AuthEmailOptions options, string to, string username, string token, DateTime expiresAtUtc)
    {
        return AuthEmailRenderer.Render(options, to, new EmailContent(
            $"Reset your {options.ProductName} password",
            "Someone asked to reset your password. If it was you, choose a new one.",
            "Reset your password",
            [
                new EmailParagraph($"Hello {username} — someone asked to reset the password for your {options.ProductName} account."),
                new EmailAction(Link(options, "reset-password", token), "Choose a new password"),
                Expiry(expiresAtUtc),
                new EmailNote("If that was not you, no action is needed and your password has not changed. Signing in everywhere else stays as it was until this link is used.")
            ]));
    }

    /// <summary>
    /// Sent when someone tries to register an address that already has an account. Registration
    /// answers identically either way, so this message is the only thing that distinguishes the two
    /// cases — and it goes to the address's real owner, not to whoever submitted the form.
    /// </summary>
    public static OutboundMail AccountAlreadyExists(AuthEmailOptions options, string to, string username)
    {
        return AuthEmailRenderer.Render(options, to, new EmailContent(
            $"Someone tried to register your {options.ProductName} account",
            "No new account was created, and nothing about yours has changed.",
            "You already have an account",
            [
                new EmailParagraph($"Hello {username} — someone just tried to create a {options.ProductName} account with this address, but you already have one."),
                new EmailParagraph("If that was you, sign in as usual. If you have forgotten your password, you can reset it."),
                new EmailAction($"{options.ConsoleBaseUrl.TrimEnd('/')}/forgot-password", "Reset your password"),
                new EmailNote("No account was created and nothing about yours has changed. If this was not you, no action is needed.")
            ]));
    }

    /// <summary>Sent to the new address of an email change. The account keeps its old address until this is followed.</summary>
    public static OutboundMail EmailChange(AuthEmailOptions options, string to, string username, string token, DateTime expiresAtUtc)
    {
        return AuthEmailRenderer.Render(options, to, new EmailContent(
            $"Confirm your new email address for {options.ProductName}",
            $"Confirm this address to sign in to {options.ProductName} with it.",
            "Confirm your new email address",
            [
                new EmailParagraph($"Hello {username} — confirm this address to make it the one you sign in to {options.ProductName} with."),
                new EmailAction(Link(options, "confirm-email-change", token), "Use this address"),
                Expiry(expiresAtUtc),
                new EmailNote("Confirming signs you out everywhere; sign in again with this address. If you did not ask for this, ignore this message and nothing changes.")
            ]));
    }

    /// <summary>
    /// Sent when someone asks to move another account to an address that already has one. The request
    /// answers as if it had gone ahead, so this goes to the address's owner instead, with no link: they
    /// asked for nothing, and there is nothing for them to do.
    /// </summary>
    public static OutboundMail EmailChangeConflict(AuthEmailOptions options, string to, string username)
    {
        return AuthEmailRenderer.Render(options, to, new EmailContent(
            $"Someone tried to use your address on another {options.ProductName} account",
            "Nothing about your account has changed.",
            "Your address is already in use",
            [
                new EmailParagraph($"Hello {username} — someone signed in to another {options.ProductName} account just asked to change its email address to this one. This address already belongs to your account, so no change was made."),
                new EmailNote("Nothing about your account has changed and no action is needed. If you expected this, sign in to the other account with its own address instead.")
            ]));
    }

    /// <summary>
    /// Sent once the password is changed or reset. Every session has just ended, so if someone else
    /// did it, this is how the owner learns why they were signed out, and the way back in. No token:
    /// the reset it points to mails its own link.
    /// </summary>
    public static OutboundMail PasswordChanged(AuthEmailOptions options, string to, string username, string how, DateTime atUtc, string ipAddress)
    {
        var verb = how == "reset" ? "reset" : "changed";
        return AuthEmailRenderer.Render(options, to, new EmailContent(
            $"Your {options.ProductName} password was {verb}",
            "If you did not do this, reset your password now.",
            $"Your password was {verb}",
            [
                new EmailParagraph($"Hello {username} — the password for your {options.ProductName} account was {verb} at {Time(atUtc)} UTC, from {ipAddress}. Every other session was signed out."),
                new EmailParagraph("If this was you, no action is needed. If it was not, someone else knows your password: reset it now."),
                new EmailAction(ForgotPassword(options), "Reset your password"),
                new EmailNote("Resetting signs the account out everywhere again.")
            ]));
    }

    /// <summary>
    /// Sent when wrong passwords lock the account. The sign-in page answers a lock like a wrong
    /// password, so it does not tell anyone that the address has an account; this tells the owner.
    /// </summary>
    public static OutboundMail AccountLocked(AuthEmailOptions options, string to, string username, DateTime lockedUntilUtc, string ipAddress)
    {
        return AuthEmailRenderer.Render(options, to, new EmailContent(
            $"Your {options.ProductName} account was locked after too many wrong passwords",
            "Sign-in is paused for a few minutes. Resetting your password lifts it at once.",
            "Your account was locked",
            [
                new EmailParagraph($"Hello {username} — there were too many wrong passwords for your {options.ProductName} account, the last from {ipAddress}, so new sign-ins are paused until {Time(lockedUntilUtc)} UTC. Sessions already signed in keep working."),
                new EmailParagraph("If that was you, wait and try again, or reset your password to get in now. If it was not, someone may be guessing your password: a reset lifts the lock and makes the guessing pointless."),
                new EmailAction(ForgotPassword(options), "Reset your password"),
                new EmailNote("A browser you signed in from before can still sign in with the right password during the lock.")
            ]));
    }

    /// <summary>
    /// Sent to the old address once an email change goes through. The way back when the change was
    /// made with a stolen session, so it names the new address and carries the undo.
    /// </summary>
    public static OutboundMail EmailChanged(AuthEmailOptions options, string to, string username, string newEmail, string revertToken, DateTime expiresAtUtc)
    {
        return AuthEmailRenderer.Render(options, to, new EmailContent(
            $"Your {options.ProductName} email address was changed",
            "If you did not make this change, you can undo it from this email.",
            "Your email address was changed",
            [
                new EmailParagraph($"Hello {username} — your {options.ProductName} account now signs in with {newEmail} instead of this address, and every session was signed out."),
                new EmailParagraph("If you made this change, no action is needed. If you did not, put this address back:"),
                new EmailAction(Link(options, "revert-email-change", revertToken), "Undo this change"),
                Expiry(expiresAtUtc),
                new EmailNote("Undoing it signs the account out everywhere again. Then reset your password, since whoever made the change may know it.")
            ]));
    }

    /// <summary>
    /// A billing notice: its paragraphs (plain text, a workspace name among them) and the way to the
    /// billing page.
    /// </summary>
    public static OutboundMail Billing(AuthEmailOptions options, string to, string username, BillingNotice notice, string billingUrl)
    {
        var blocks = new List<EmailBlock>
        {
            new EmailParagraph($"Hello {username} — this is about your {options.ProductName} account.")
        };
        blocks.AddRange(notice.Paragraphs.Select(p => new EmailParagraph(p)));
        blocks.Add(new EmailAction(billingUrl, "Open billing"));
        blocks.Add(new EmailNote("You get this because you manage billing for the account. We send each notice once."));

        string preheader = notice.Paragraphs.Count > 0 ? notice.Paragraphs[0] : notice.Heading;
        return AuthEmailRenderer.Render(options, to, new EmailContent(notice.Subject, preheader, notice.Heading, blocks));
    }

    /// <summary>
    /// Tokens from <see cref="SecurityTokens.Generate"/> are already URL-safe base64, so this escape
    /// changes nothing today. It is here so the link stays correct if the generator ever does.
    /// </summary>
    private static string Link(AuthEmailOptions options, string path, string token) =>
        $"{options.ConsoleBaseUrl.TrimEnd('/')}/{path}?token={Uri.EscapeDataString(token)}";

    private static string ForgotPassword(AuthEmailOptions options) => $"{options.ConsoleBaseUrl.TrimEnd('/')}/forgot-password";

    private static string Time(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static EmailNote Expiry(DateTime expiresAtUtc) =>
        new($"This link stops working at {expiresAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC, and can only be used once.");
}
