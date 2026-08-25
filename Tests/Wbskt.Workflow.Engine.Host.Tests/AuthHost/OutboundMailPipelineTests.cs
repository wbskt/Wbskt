using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.Infrastructure.Email;

namespace Wbskt.Workflow.Engine.Host.Tests.AuthHost;

/// <summary>
/// The queue and the dispatcher that carry every message this host sends.
///
/// Worth testing on its own because the failure mode is silent: mail that is dropped, or retried
/// forever, or abandoned on the first hiccup, produces no error anyone sees. And since sign-in now
/// requires a confirmed address, "mail quietly stopped working" and "nobody can sign up" are the
/// same outage.
/// </summary>
public sealed class OutboundMailPipelineTests
{
    private static OutboundMail Message(string to = "someone@example.test") => new(to, "Subject", "<p>Body</p>");

    // ---------------------------------------------------------------- queue

    [Fact]
    public async Task The_queue_hands_back_what_was_written()
    {
        var queue = new OutboundMailQueue();
        Assert.True(queue.TryWrite(Message("first@example.test")));

        Assert.True(TryDrain(queue, out var mail));
        Assert.Equal("first@example.test", mail!.To);

        await Task.CompletedTask;
    }

    /// <summary>
    /// A full queue must refuse, visibly, rather than swallow. The Drop* channel modes return true
    /// while discarding the message, which would make every drop invisible to the caller that is
    /// supposed to log it — this test exists because that is exactly what the first version did.
    /// </summary>
    [Fact]
    public void The_queue_drops_rather_than_blocking_when_it_is_full()
    {
        var queue = new OutboundMailQueue();

        int accepted = 0;
        for (int i = 0; i < 1_500; i++)
        {
            if (queue.TryWrite(Message()))
            {
                accepted++;
            }
        }

        // Nothing drains it here, so the surplus is refused rather than queued, awaited, or silently
        // dropped. What matters is that TryWrite says false: a caller that has to wait is a
        // registration that has to wait, and a caller that is told "true" cannot log the loss.
        Assert.Equal(OutboundMailQueue.Capacity, accepted);
        Assert.False(queue.TryWrite(Message()));
    }

    // ---------------------------------------------------------------- dispatcher

    [Fact]
    public async Task The_dispatcher_sends_queued_mail_as_html()
    {
        var sender = new ScriptedSender();
        await using var harness = new DispatcherHarness(sender);

        await harness.StartAsync();
        harness.Queue.TryWrite(new OutboundMail("someone@example.test", "Confirm your address", "<p>Hello</p>"));

        var sent = await sender.WaitForNextAsync();

        Assert.Equal("someone@example.test", sent.To);
        Assert.Equal("Confirm your address", sent.Subject);
        Assert.Equal("<p>Hello</p>", sent.Body);

        // Html, unlike the workflow email node. These bodies are ours and are built from templates in
        // this repository, which is the whole reason the format is a per-send argument.
        Assert.Equal(EmailBodyFormat.Html, sent.Format);
    }

    [Fact]
    public async Task A_transient_failure_is_retried_and_the_message_still_goes()
    {
        var sender = new ScriptedSender(failures: 2, () => new IOException("relay closed the connection"));
        await using var harness = new DispatcherHarness(sender);

        await harness.StartAsync();
        harness.Queue.TryWrite(Message());

        await sender.WaitForNextAsync();

        // Two failures then a success: the ladder is two delays, so three attempts are available.
        Assert.Equal(3, sender.Attempts);
    }

    [Fact]
    public async Task A_permanently_broken_relay_gives_up_after_three_attempts()
    {
        var sender = new ScriptedSender(failures: int.MaxValue, () => new IOException("relay is down"));
        await using var harness = new DispatcherHarness(sender);

        await harness.StartAsync();
        harness.Queue.TryWrite(Message());

        await sender.WaitForAttemptsAsync(3);

        // The loop is sequential, so an unbounded retry on one message would stall every message
        // behind it. Give the dispatcher room to make a fourth attempt if it were going to.
        await Task.Delay(150);
        Assert.Equal(3, sender.Attempts);
    }

    /// <summary>
    /// A malformed address fails identically on every attempt, so retrying it only delays the
    /// messages queued behind it.
    /// </summary>
    [Fact]
    public async Task A_malformed_address_is_not_retried()
    {
        var sender = new ScriptedSender(failures: int.MaxValue, () => new FormatException("not an address"));
        await using var harness = new DispatcherHarness(sender);

        await harness.StartAsync();
        harness.Queue.TryWrite(Message("not-an-address"));

        await sender.WaitForAttemptsAsync(1);
        await Task.Delay(150);

        Assert.Equal(1, sender.Attempts);
    }

    /// <summary>
    /// One wedged message must not become a stuck queue. The next one goes out regardless.
    /// </summary>
    [Fact]
    public async Task A_message_that_could_not_be_sent_does_not_block_the_next_one()
    {
        var sender = new ScriptedSender(failures: 1, () => new IOException("one bad send"));
        await using var harness = new DispatcherHarness(sender);

        await harness.StartAsync();
        harness.Queue.TryWrite(Message("first@example.test"));
        harness.Queue.TryWrite(Message("second@example.test"));

        await sender.WaitForAttemptsAsync(3);

        Assert.Equal(
            ["first@example.test", "first@example.test", "second@example.test"],
            sender.Recipients);
    }

    // ---------------------------------------------------------------- mailer

    [Fact]
    public async Task The_mailer_queues_nothing_when_no_relay_is_configured()
    {
        var queue = new OutboundMailQueue();
        var mailer = NewMailer(queue, configured: false);

        await mailer.QueuePasswordResetAsync("someone@example.test", "someone", "tok", DateTime.UtcNow.AddHours(1), CancellationToken.None);

        Assert.False(mailer.IsConfigured);
        Assert.False(TryDrain(queue, out _));
    }

    /// <summary>
    /// Every caller is midway through an operation that has already succeeded — an account created, a
    /// reset issued. Failing that because the relay is unreachable would undo work the user can see,
    /// for a reason they cannot act on.
    /// </summary>
    [Fact]
    public async Task The_mailer_never_throws_at_its_caller()
    {
        var queue = new OutboundMailQueue();
        var mailer = NewMailer(queue, configured: false);

        // All four, because a throw from any one of them takes down a different endpoint.
        await mailer.QueueInvitationAsync("a@example.test", "Acme", "t", DateTime.UtcNow, CancellationToken.None);
        await mailer.QueueEmailVerificationAsync("a@example.test", "a", "t", DateTime.UtcNow, CancellationToken.None);
        await mailer.QueuePasswordResetAsync("a@example.test", "a", "t", DateTime.UtcNow, CancellationToken.None);
        await mailer.QueueAccountAlreadyExistsAsync("a@example.test", "a", CancellationToken.None);
    }

    [Fact]
    public async Task The_mailer_renders_before_queueing_so_no_raw_token_is_queued()
    {
        var queue = new OutboundMailQueue();
        var mailer = NewMailer(queue, configured: true);

        await mailer.QueuePasswordResetAsync("someone@example.test", "someone", "the-raw-token", DateTime.UtcNow.AddHours(1), CancellationToken.None);

        Assert.True(TryDrain(queue, out var mail));

        // The token exists only inside the rendered body by this point — there is no field on the
        // queued item holding it, which is what keeps it out of anything that logs the queue.
        Assert.Contains("the-raw-token", mail!.HtmlBody);
        Assert.DoesNotContain("the-raw-token", mail.To);
        Assert.DoesNotContain("the-raw-token", mail.Subject);
    }

    // ---------------------------------------------------------------- support

    private static QueuedAuthMailer NewMailer(OutboundMailQueue queue, bool configured) =>
        new(queue,
            new ScriptedSender { Configured = configured },
            Options.Create(new AuthEmailOptions { ConsoleBaseUrl = "https://console.example.test" }),
            NullLogger<QueuedAuthMailer>.Instance);

    private static bool TryDrain(OutboundMailQueue queue, out OutboundMail? mail)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        try
        {
            var enumerator = queue.ReadAllAsync(cts.Token).GetAsyncEnumerator(cts.Token);
            if (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
            {
                mail = enumerator.Current;
                return true;
            }
        }
        catch (OperationCanceledException)
        {
            // Nothing queued within the window.
        }

        mail = null;
        return false;
    }

    /// <summary>Starts a real dispatcher over a real queue, with a retry ladder short enough to test.</summary>
    private sealed class DispatcherHarness : IAsyncDisposable
    {
        private readonly OutboundMailDispatcher _dispatcher;

        public DispatcherHarness(ScriptedSender sender)
        {
            Queue = new OutboundMailQueue();
            _dispatcher = new OutboundMailDispatcher(
                Queue,
                sender,
                NullLogger<OutboundMailDispatcher>.Instance,
                retryDelays: [TimeSpan.Zero, TimeSpan.Zero]);
        }

        public OutboundMailQueue Queue { get; }

        public Task StartAsync() => _dispatcher.StartAsync(CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            await _dispatcher.StopAsync(CancellationToken.None);
            _dispatcher.Dispose();
        }
    }

    private sealed class ScriptedSender(int failures = 0, Func<Exception>? failWith = null) : IEmailSender
    {
        private readonly Lock _gate = new();
        private readonly List<(string To, string Subject, string Body, EmailBodyFormat Format)> _sent = [];
        private TaskCompletionSource<(string To, string Subject, string Body, EmailBodyFormat Format)> _next = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _remainingFailures = failures;

        public bool Configured { get; init; } = true;

        public bool IsConfigured => Configured;

        public int Attempts { get; private set; }

        public IReadOnlyList<string> Recipients
        {
            get { lock (_gate) { return _attempted.ToArray(); } }
        }

        private readonly List<string> _attempted = [];

        public Task SendAsync(string to, string subject, string body, EmailBodyFormat format, CancellationToken ct)
        {
            lock (_gate)
            {
                Attempts++;
                _attempted.Add(to);

                if (_remainingFailures > 0)
                {
                    _remainingFailures--;
                    throw (failWith ?? (() => new IOException("send failed")))();
                }

                _sent.Add((to, subject, body, format));
                _next.TrySetResult((to, subject, body, format));
            }

            return Task.CompletedTask;
        }

        public async Task<(string To, string Subject, string Body, EmailBodyFormat Format)> WaitForNextAsync()
        {
            var completed = await Task.WhenAny(_next.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(_next.Task, completed);
            return await _next.Task;
        }

        public async Task WaitForAttemptsAsync(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                lock (_gate)
                {
                    if (Attempts >= count)
                    {
                        return;
                    }
                }

                await Task.Delay(10);
            }

            Assert.Fail($"Only {Attempts} of {count} expected attempts were made.");
        }
    }
}
