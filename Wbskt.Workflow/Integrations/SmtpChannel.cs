using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Engine.Contracts;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Integrations;

/// <summary>
/// A mail relay, over MailKit so both STARTTLS (587) and implicit TLS (465) work. The relay host is
/// the owner's choice, so it goes through the address guard.
/// </summary>
internal sealed class SmtpChannel(IOutboundAddressGuard addressGuard, IOptions<IntegrationOptions> options) : IIntegrationChannel
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public IntegrationType Type => IntegrationType.Smtp;

    public async Task<ChannelResult> TestAsync(ResolvedIntegration integration, IntegrationTestOptions? testOptions, CancellationToken ct)
    {
        MimeMessage? message = null;
        if (!string.IsNullOrWhiteSpace(testOptions?.SendTo))
        {
            var built = Build(integration, testOptions.SendTo, "Test email from Wbskt", "This relay can send your workflow's email.");
            if (built.Failure is not null)
            {
                return built.Failure.ForTest();
            }

            message = built.Message;
        }

        var failure = await SendCoreAsync(integration, message, ct);
        return failure is null ? ChannelResult.Success() : Scrubbed(failure.ForTest(), integration);
    }

    public async Task<ChannelResult> SendAsync(ResolvedIntegration integration, string to, string subject, string body, CancellationToken ct)
    {
        var built = Build(integration, to, subject, body);
        if (built.Failure is not null)
        {
            return built.Failure.ForStep("EMAIL");
        }

        var failure = await SendCoreAsync(integration, built.Message, ct);
        return failure is null ? ChannelResult.Success() : Scrubbed(failure.ForStep("EMAIL"), integration);
    }

    /// <summary>
    /// Plain text, deliberately: the body comes from a workflow author and is not sanitised anywhere,
    /// so rendering it as HTML in a recipient's client would be handing them author-controlled markup.
    /// </summary>
    private static (MimeMessage? Message, ChannelFailure? Failure) Build(ResolvedIntegration integration, string to, string subject, string body)
    {
        var settings = (SmtpSettings)integration.Settings;
        if (!InternetAddressList.TryParse(to, out var recipients) || recipients.Count == 0 || recipients.Mailboxes.Any(m => !m.Address.Contains('@')))
        {
            return (null, new ChannelFailure(ChannelFailureKind.AddressInvalid, $"'{to}' is not an email address.", false));
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName ?? string.Empty, settings.FromAddress));
        message.To.AddRange(recipients);
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };
        return (message, null);
    }

    /// <summary>Connects, signs in, sends <paramref name="message"/> when there is one, and disconnects.</summary>
    private async Task<ChannelFailure?> SendCoreAsync(ResolvedIntegration integration, MimeMessage? message, CancellationToken ct)
    {
        var settings = (SmtpSettings)integration.Settings;

        // The guard vets http(s) URLs; the relay's host and port are put in one only to be resolved.
        var decision = await addressGuard.EvaluateAsync(new Uri($"http://{settings.Host}:{settings.Port}/"), ct);
        if (!decision.Allowed)
        {
            return new ChannelFailure(ChannelFailureKind.BlockedTarget, $"The mail relay is at an address this engine may not call ({decision.Reason}).", false);
        }

        var socketOptions = settings.Tls == SmtpSettings.Implicit
            ? SecureSocketOptions.SslOnConnect
            : options.Value.AllowInsecureTransport ? SecureSocketOptions.StartTlsWhenAvailable : SecureSocketOptions.StartTls;

        using var client = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
        try
        {
            await client.ConnectAsync(settings.Host, settings.Port!.Value, socketOptions, ct);
            if (settings.UserName is not null)
            {
                await client.AuthenticateAsync(settings.UserName, integration.Secrets.Password, ct);
            }

            if (message is not null)
            {
                await client.SendAsync(message, ct);
            }

            await client.DisconnectAsync(true, ct);
            return null;
        }
        catch (AuthenticationException)
        {
            return new ChannelFailure(ChannelFailureKind.AuthFailed, "The mail relay refused the user name and password.", false);
        }
        catch (SslHandshakeException)
        {
            return new ChannelFailure(ChannelFailureKind.TlsFailed, $"Could not set up TLS with the mail relay ({settings.Tls} on port {settings.Port}).", false);
        }
        catch (NotSupportedException)
        {
            // STARTTLS required but the relay does not offer it.
            return new ChannelFailure(ChannelFailureKind.TlsFailed, "The mail relay does not offer STARTTLS.", false);
        }
        catch (SmtpCommandException ex) when (ex.ErrorCode is SmtpErrorCode.RecipientNotAccepted)
        {
            return new ChannelFailure(ChannelFailureKind.RecipientRefused, $"The mail relay refused the recipient {ex.Mailbox?.Address} ({(int)ex.StatusCode}).", false, (int)ex.StatusCode);
        }
        catch (SmtpCommandException ex)
        {
            // 4xx is the relay asking for a retry (greylisting, a full queue); 5xx is final.
            int status = (int)ex.StatusCode;
            return new ChannelFailure(ChannelFailureKind.SendError, $"The mail relay refused the message ({status}).", status < 500, status);
        }
        catch (Exception ex) when (ex is SocketException or IOException or ServiceNotConnectedException or SmtpProtocolException or TimeoutException
                                       || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return new ChannelFailure(ChannelFailureKind.NetworkError, $"Could not reach the mail relay at {settings.Host}:{settings.Port}.", true);
        }
    }

    private static ChannelResult Scrubbed(ChannelResult result, ResolvedIntegration integration) =>
        result with { Message = IntegrationErrors.Scrub(result.Message, integration.Secrets) };
}
