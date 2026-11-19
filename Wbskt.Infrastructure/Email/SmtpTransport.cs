using MailKit.Security;
using MimeKit;

namespace Wbskt.Infrastructure.Email;

/// <summary>
/// The SMTP rules the platform's own mail (<see cref="SmtpEmailSender"/>) and a workspace's SMTP
/// integration share: which port means TLS from the start, how that becomes MailKit's socket option,
/// and what counts as a recipient address.
/// </summary>
public static class SmtpTransport
{
    public const int ImplicitTlsPort = 465;

    /// <summary>Whether a relay on <paramref name="port"/> speaks TLS from the start (465) rather than upgrading with STARTTLS.</summary>
    public static bool ImpliesImplicitTls(int port) => port == ImplicitTlsPort;

    /// <summary>
    /// TLS from the start for an implicit-TLS relay; otherwise STARTTLS, required unless
    /// <paramref name="withoutStartTls"/> says what to do instead.
    /// </summary>
    public static SecureSocketOptions SocketOptions(bool implicitTls, bool requireStartTls, SecureSocketOptions withoutStartTls) =>
        implicitTls ? SecureSocketOptions.SslOnConnect
        : requireStartTls ? SecureSocketOptions.StartTls
        : withoutStartTls;

    /// <summary>The recipients in <paramref name="to"/> (one address or a comma-separated list), or false when it is not one.</summary>
    public static bool TryParseRecipients(string to, out InternetAddressList recipients) =>
        InternetAddressList.TryParse(to, out recipients) && recipients.Count > 0 && recipients.Mailboxes.All(m => m.Address.Contains('@'));
}
