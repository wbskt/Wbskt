using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace Wbskt.Infrastructure.Email;

/// <summary>
/// Sends through a plain SMTP relay configured on the host.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    public SmtpEmailSender(IOptions<EmailOptions>? options = null)
    {
        _options = options?.Value ?? new EmailOptions();
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task SendAsync(string to, string subject, string body, EmailBodyFormat format, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("No SMTP host or from-address is configured for this host's email section.");
        }

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseStartTls
        };

        // Anonymous relays are legitimate on an internal network, so credentials are optional; sending
        // the machine's own Windows credentials to an external relay, which is what UseDefaultCredentials
        // would do, is not.
        client.UseDefaultCredentials = false;
        if (!string.IsNullOrWhiteSpace(_options.UserName))
        {
            client.Credentials = new NetworkCredential(_options.UserName, _options.Password);
        }

        using var message = new MailMessage
        {
            From = string.IsNullOrWhiteSpace(_options.FromDisplayName)
                ? new MailAddress(_options.FromAddress!)
                : new MailAddress(_options.FromAddress!, _options.FromDisplayName),
            Subject = subject,
            Body = body,
            IsBodyHtml = format == EmailBodyFormat.Html
        };

        // Multiple recipients are ordinary; MailAddressCollection parses a comma-separated list.
        message.To.Add(to);

        await client.SendMailAsync(message, ct);
    }
}
