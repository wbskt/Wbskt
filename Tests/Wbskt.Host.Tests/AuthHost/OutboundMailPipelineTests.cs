using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics.Metrics;
using System.Net.Mail;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.Infrastructure.Email;

namespace Wbskt.Host.Tests.AuthHost;

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
    private static OutboundMail Message(string to = "someone@example.test") => new(to, "Subject", "Body", "<p>Body</p>");

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
    public async Task The_dispatcher_sends_queued_mail_as_text_with_an_html_alternative()
    {
        var sender = new ScriptedSender();
        await using var harness = new DispatcherHarness(sender);

        await harness.StartAsync();
        harness.Queue.TryWrite(new OutboundMail("someone@example.test", "Confirm your address", "Hello", "<p>Hello</p>"));

        var sent = await sender.WaitForNextAsync();

        Assert.Equal("someone@example.test", sent.To);
        Assert.Equal("Confirm your address", sent.Subject);
        Assert.Equal("Hello", sent.Text);

        // Html, unlike the workflow email node. These bodies are ours and are built from templates in
        // this repository, which is the whole reason the format is a per-send argument.
        Assert.Equal("<p>Hello</p>", sent.Html);
    }

    /// <summary>
    /// Without a line on success, "the relay took it and it went missing later" and "it was never
    /// handed over" read the same in the logs. The body carries the token and must never be logged.
    /// </summary>
    [Fact]
    public async Task A_sent_message_is_logged_without_its_body()
    {
        var sender = new ScriptedSender();
        var logger = new RecordingLogger();
        await using var harness = new DispatcherHarness(sender, logger);

        await harness.StartAsync();
        harness.Queue.TryWrite(new OutboundMail("someone@example.test", "Reset your password", "the-raw-token", "<p>the-raw-token</p>"));
        await sender.WaitForNextAsync();

        var line = await logger.WaitForAsync(m => m.Contains("accepted by the relay"));
        Assert.Contains(EmailFingerprint.Of("someone@example.test"), line);
        Assert.DoesNotContain(logger.Messages, m => m.Contains("the-raw-token"));
        Assert.DoesNotContain(logger.Messages, m => m.Contains("someone@example.test"));
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
    /// #778: a relay that refuses mail must show on a dashboard and raise MailFailing, not only leave
    /// an error line in the logs. Each attempt tried again is counted, and the message given up on.
    /// </summary>
    [Fact]
    public async Task Mail_given_up_on_is_counted_as_dropped_and_each_attempt_tried_again_as_retried()
    {
        var sender = new ScriptedSender(failures: 3, () => new IOException("relay is down"));
        await using var harness = new DispatcherHarness(sender);
        using var outcomes = new MailOutcomes(harness.Metrics);

        await harness.StartAsync();
        harness.Queue.TryWrite(Message("first@example.test"));
        harness.Queue.TryWrite(Message("second@example.test"));
        await sender.WaitForNextAsync();
        await outcomes.WaitForAsync(4);

        Assert.Equal([MailOutcome.Dropped, MailOutcome.Retried, MailOutcome.Retried, MailOutcome.Sent], outcomes.Seen.Order());
    }

    [Fact]
    public async Task A_full_queue_counts_the_refused_message_as_queue_full()
    {
        var queue = new OutboundMailQueue();
        var metrics = new AuthMetrics();
        using var outcomes = new MailOutcomes(metrics);
        var mailer = NewMailer(queue, configured: true, metrics);
        while (queue.TryWrite(Message()))
        {
        }

        await mailer.QueuePasswordResetAsync("someone@example.test", "someone", "tok", DateTime.UtcNow.AddHours(1), CancellationToken.None);

        Assert.Equal([MailOutcome.QueueFull], outcomes.Seen);
    }

    /// <summary>
    /// A recipient the relay refuses for good is one bad mailbox, not the relay failing: it is not
    /// retried, does not page, and the relay's reply, which repeats the address, stays out of the log.
    /// </summary>
    [Fact]
    public async Task A_recipient_the_relay_refuses_for_good_is_counted_as_rejected_and_not_retried()
    {
        var sender = new ScriptedSender(failures: int.MaxValue, () => new SmtpFailedRecipientException(SmtpStatusCode.MailboxUnavailable, "<gone@example.test>"));
        var logger = new RecordingLogger();
        await using var harness = new DispatcherHarness(sender, logger);
        using var outcomes = new MailOutcomes(harness.Metrics);

        await harness.StartAsync();
        harness.Queue.TryWrite(Message("gone@example.test"));
        await outcomes.WaitForAsync(1);
        await Task.Delay(150);

        Assert.Equal(1, sender.Attempts);
        Assert.Equal([MailOutcome.Rejected], outcomes.Seen);
        Assert.DoesNotContain(logger.Messages, m => m.Contains("gone@example.test"));
    }

    [Fact]
    public async Task A_malformed_address_is_counted_as_invalid_address()
    {
        var sender = new ScriptedSender(failures: int.MaxValue, () => new FormatException("not an address"));
        await using var harness = new DispatcherHarness(sender);
        using var outcomes = new MailOutcomes(harness.Metrics);

        await harness.StartAsync();
        harness.Queue.TryWrite(Message("not-an-address"));
        await outcomes.WaitForAsync(1);

        Assert.Equal([MailOutcome.InvalidAddress], outcomes.Seen);
    }

    /// <summary>A malformed from-address fails every message alike, so it is the relay failing, not one recipient.</summary>
    [Fact]
    public async Task A_format_error_with_a_good_recipient_is_retried_and_dropped()
    {
        var sender = new ScriptedSender(failures: int.MaxValue, () => new FormatException("bad from-address"));
        await using var harness = new DispatcherHarness(sender);
        using var outcomes = new MailOutcomes(harness.Metrics);

        await harness.StartAsync();
        harness.Queue.TryWrite(Message("someone@example.test"));
        await outcomes.WaitForAsync(3);

        Assert.Equal(3, sender.Attempts);
        Assert.Contains(MailOutcome.Dropped, outcomes.Seen);
    }

    [Fact]
    public void The_queue_reports_its_depth()
    {
        var metrics = new AuthMetrics();
        var queue = new OutboundMailQueue(metrics);
        queue.TryWrite(Message());
        queue.TryWrite(Message());
        long depth = -1;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == AuthMetrics.MeterName && instrument.Name == "wbskt_auth_mail_queue_depth")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<int>((_, value, _, _) => depth = value);
        listener.Start();

        listener.RecordObservableInstruments();

        Assert.Equal(2, depth);
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
        Assert.Contains("the-raw-token", mail.TextBody);
        Assert.DoesNotContain("the-raw-token", mail.To);
        Assert.DoesNotContain("the-raw-token", mail.Subject);
    }

    // ---------------------------------------------------------------- support

    private static QueuedAuthMailer NewMailer(OutboundMailQueue queue, bool configured, AuthMetrics? metrics = null) =>
        new(queue,
            new ScriptedSender { Configured = configured },
            Options.Create(new AuthEmailOptions { ConsoleBaseUrl = "https://console.example.test" }),
            NullLogger<QueuedAuthMailer>.Instance,
            metrics ?? new AuthMetrics());

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

    /// <summary>The outcome of every mail one <see cref="AuthMetrics"/> counts from now on, skipping the zeros it starts at.</summary>
    private sealed class MailOutcomes : IDisposable
    {
        private readonly MeterListener _listener;
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _seen = new();

        public MailOutcomes(AuthMetrics metrics)
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, l) =>
                {
                    if (ReferenceEquals(instrument, metrics.Mail))
                    {
                        l.EnableMeasurementEvents(instrument);
                    }
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                for (int i = 0; i < value; i++)
                {
                    _seen.Enqueue((string)tags[0].Value!);
                }
            });
            _listener.Start();
        }

        public IReadOnlyCollection<string> Seen => _seen;

        public async Task WaitForAsync(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (_seen.Count < count)
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException($"Only {_seen.Count} of {count} mail outcomes were counted.");
                }

                await Task.Delay(10);
            }
        }

        public void Dispose() => _listener.Dispose();
    }

    private sealed class RecordingLogger : ILogger<OutboundMailDispatcher>
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _messages.Enqueue(formatter(state, exception));

        /// <summary>The dispatcher logs after the send returns, so the line can trail the send by a moment.</summary>
        public async Task<string> WaitForAsync(Func<string, bool> match)
        {
            for (int i = 0; i < 200; i++)
            {
                if (_messages.FirstOrDefault(match) is { } line) return line;
                await Task.Delay(10);
            }

            throw new TimeoutException("The expected log line never arrived.");
        }
    }

    private sealed class DispatcherHarness : IAsyncDisposable
    {
        private readonly OutboundMailDispatcher _dispatcher;

        public DispatcherHarness(ScriptedSender sender, ILogger<OutboundMailDispatcher>? logger = null)
        {
            Queue = new OutboundMailQueue();
            _dispatcher = new OutboundMailDispatcher(
                Queue,
                sender,
                logger ?? NullLogger<OutboundMailDispatcher>.Instance,
                Metrics,
                retryDelays: [TimeSpan.Zero, TimeSpan.Zero]);
        }

        public OutboundMailQueue Queue { get; }

        public AuthMetrics Metrics { get; } = new();

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
        private readonly List<(string To, string Subject, string Text, string Html)> _sent = [];
        private TaskCompletionSource<(string To, string Subject, string Text, string Html)> _next = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _remainingFailures = failures;

        public bool Configured { get; init; } = true;

        public bool IsConfigured => Configured;

        public int Attempts { get; private set; }

        public IReadOnlyList<string> Recipients
        {
            get { lock (_gate) { return _attempted.ToArray(); } }
        }

        private readonly List<string> _attempted = [];

        public Task SendAsync(string to, string subject, string body, EmailBodyFormat format, CancellationToken ct) =>
            throw new InvalidOperationException("Auth mail goes out as multipart, never as a single body.");

        public Task SendMultipartAsync(string to, string subject, string textBody, string htmlBody, CancellationToken ct)
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

                _sent.Add((to, subject, textBody, htmlBody));
                _next.TrySetResult((to, subject, textBody, htmlBody));
            }

            return Task.CompletedTask;
        }

        public async Task<(string To, string Subject, string Text, string Html)> WaitForNextAsync()
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
