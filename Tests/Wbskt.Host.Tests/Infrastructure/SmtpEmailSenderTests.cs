using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Wbskt.Infrastructure.Email;

namespace Wbskt.Host.Tests.Infrastructure;

/// <summary>A relay that stops answering costs one send its timeout, not the mail drain behind it.</summary>
public sealed class SmtpEmailSenderTests
{
    [Fact]
    public async Task A_relay_that_never_answers_fails_the_send_with_a_timeout()
    {
        using var relay = new TcpListener(IPAddress.Loopback, 0);
        relay.Start();
        var accepting = relay.AcceptTcpClientAsync();
        var sender = new SmtpEmailSender(Options.Create(new EmailOptions
        {
            Host = "127.0.0.1",
            Port = ((IPEndPoint)relay.LocalEndpoint).Port,
            UseStartTls = false,
            FromAddress = "noreply@example.test",
            SendTimeout = TimeSpan.FromMilliseconds(300)
        }));

        var send = sender.SendAsync("someone@example.test", "subject", "body", EmailBodyFormat.PlainText, CancellationToken.None);

        await Assert.ThrowsAsync<TimeoutException>(() => send.WaitAsync(TimeSpan.FromSeconds(10)));
        (await accepting).Dispose();
    }

    [Fact]
    public async Task Stopping_the_host_cancels_rather_than_times_out()
    {
        using var relay = new TcpListener(IPAddress.Loopback, 0);
        relay.Start();
        var accepting = relay.AcceptTcpClientAsync();
        using var stopping = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var sender = new SmtpEmailSender(Options.Create(new EmailOptions
        {
            Host = "127.0.0.1",
            Port = ((IPEndPoint)relay.LocalEndpoint).Port,
            UseStartTls = false,
            FromAddress = "noreply@example.test",
            SendTimeout = TimeSpan.FromMinutes(1)
        }));

        var send = sender.SendAsync("someone@example.test", "subject", "body", EmailBodyFormat.PlainText, stopping.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send.WaitAsync(TimeSpan.FromSeconds(10)));
        (await accepting).Dispose();
    }
}
