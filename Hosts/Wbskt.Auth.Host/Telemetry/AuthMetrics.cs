using System.Diagnostics;
using System.Diagnostics.Metrics;
using Wbskt.Infrastructure.Telemetry;

namespace Wbskt.Auth.Host.Telemetry;

public sealed class AuthMetrics : IWorkerTickMetrics, IDisposable
{
    public const string MeterName = "Wbskt.Auth";
    private readonly Meter _meter;

    // Every series BillingReconcileFailing watches, so each starts at zero.
    private static readonly (string Kind, string Outcome)[] ReconcileAlerted =
    [
        ("subscription", "failed"), ("payment", "failed"),
        ("subscription_list", "failed"), ("payment_list", "failed"),
        ("subscription_list", "truncated"), ("payment_list", "truncated")
    ];

    public AuthMetrics()
    {
        _meter = new Meter(MeterName);
        
        LoginRequests = _meter.CreateCounter<long>("wbskt_auth_login_requests_total");
        RefreshRequests = _meter.CreateCounter<long>("wbskt_auth_refresh_requests_total");
        Registrations = _meter.CreateCounter<long>("wbskt_auth_registrations_total");
        
        WorkspaceResolutions = _meter.CreateCounter<long>("wbskt_auth_workspace_resolutions_total");
        PermissionChecks = _meter.CreateCounter<long>("wbskt_auth_permission_checks_total");
        MailSuppressed = _meter.CreateCounter<long>("wbskt_auth_mail_suppressed_total");
        Mail = _meter.CreateCounter<long>("wbskt_auth_mail_total");
        BreachChecks = _meter.CreateCounter<long>("wbskt_auth_breached_password_checks_total");
        BillingWebhooks = _meter.CreateCounter<long>("wbskt_auth_billing_webhooks_total");
        BillingRefunds = _meter.CreateCounter<long>("wbskt_auth_billing_refunds_total");
        BillingReconciles = _meter.CreateCounter<long>("wbskt_auth_billing_reconciles_total");

        // The series the alerts watch start at zero, so the first one counted is an increase: a series that
        // first appears at 1 has no earlier sample, and increase() would miss it.
        RecordBillingRefund("unconfirmed", 0);
        Mail.Add(0, new KeyValuePair<string, object?>("outcome", MailOutcome.Dropped));
        Mail.Add(0, new KeyValuePair<string, object?>("outcome", MailOutcome.QueueFull));
        Mail.Add(0, new KeyValuePair<string, object?>("outcome", MailOutcome.Rejected));
        Mail.Add(0, new KeyValuePair<string, object?>("outcome", MailOutcome.Sent));
        BillingWebhooks.Add(0, new KeyValuePair<string, object?>("type", "unknown"), new KeyValuePair<string, object?>("outcome", "signature_invalid"));
        foreach ((string kind, string outcome) in ReconcileAlerted)
        {
            BillingReconciles.Add(0, new KeyValuePair<string, object?>("kind", kind), new KeyValuePair<string, object?>("outcome", outcome));
        }
        
        PasswordHashDuration = _meter.CreateHistogram<double>("wbskt_auth_password_hash_duration_ms");
        WorkerTicks = _meter.CreateCounter<long>("wbskt_auth_worker_ticks_total");
    }

    public Counter<long> LoginRequests { get; }
    public Counter<long> RefreshRequests { get; }
    public Counter<long> Registrations { get; }
    public Counter<long> WorkspaceResolutions { get; }
    public Counter<long> PermissionChecks { get; }
    public Counter<long> MailSuppressed { get; }

    /// <summary>
    /// Outbound mail, tagged <c>outcome</c> (one of <see cref="MailOutcome"/>). Each counts a message,
    /// except retried, which counts attempts. MailFailing watches dropped and queue_full, and
    /// MailRelayDown those with no sent beside them.
    /// </summary>
    public Counter<long> Mail { get; }

    /// <summary>Breached-password lookups, tagged <c>outcome</c>: clean, breached, or unavailable (accepted unchecked).</summary>
    public Counter<long> BreachChecks { get; }
    public Histogram<double> PasswordHashDuration { get; }

    /// <summary>
    /// Payment webhooks acted on, tagged <c>type</c> (the provider's event type) and <c>outcome</c>: done,
    /// set_aside (nothing here to act on, which for a paying customer means a tenant left on the wrong
    /// plan), failed (delivered again by the provider), signature_invalid (a signature that does not hold up)
    /// or signature_missing (none at all); the last two are refused unread, so their type is <c>unknown</c>.
    /// </summary>
    public Counter<long> BillingWebhooks { get; }

    /// <summary>
    /// Refunds of a duplicate subscription's payment, tagged <c>outcome</c>: done, refused (nothing was
    /// refunded, so the next delivery asks again) or unconfirmed (it may have gone through; an operator checks).
    /// </summary>
    public Counter<long> BillingRefunds { get; }

    /// <summary>
    /// Subscriptions and payments the daily reconcile read again from the provider, tagged <c>kind</c>
    /// (subscription or payment) and <c>outcome</c>: done, set_aside (nothing here to act on) or failed (read
    /// again next day while still in the window). Its lists are counted as kind subscription_list or
    /// payment_list: failed when the provider refused one, truncated when one was cut short.
    /// </summary>
    public Counter<long> BillingReconciles { get; }

    /// <summary>Ticks of the periodic background workers, tagged <c>worker</c> and <c>outcome</c> (ok or failed).</summary>
    public Counter<long> WorkerTicks { get; }

    public void RecordBillingRefund(string outcome, long count = 1) =>
        BillingRefunds.Add(count, new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordBillingReconcile(string kind, string outcome) =>
        BillingReconciles.Add(1, new KeyValuePair<string, object?>("kind", kind), new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordBillingWebhook(string type, string outcome) =>
        BillingWebhooks.Add(1, new KeyValuePair<string, object?>("type", type), new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordLogin(string outcome) => 
        LoginRequests.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordRefresh(string outcome) => 
        RefreshRequests.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordRegistration(string outcome) => 
        Registrations.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    // Deliberately not labelled by workspace: the ref is caller-supplied and is recorded even when it
    // resolves to nothing, so using it as a label would let anyone mint unbounded time series by
    // posting random GUIDs. Per-workspace attribution belongs in the logs, which are not cardinality
    // constrained.
    public void RecordWorkspaceResolution(string outcome) =>
        WorkspaceResolutions.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordPermissionCheck(string permissionSlug, string outcome) => 
        PermissionChecks.Add(1, 
            new KeyValuePair<string, object?>("permission_slug", permissionSlug), 
            new KeyValuePair<string, object?>("outcome", outcome));

    /// <summary>A mail the per-address cooldown dropped. A steady count is someone aiming our mail at an inbox.</summary>
    public void RecordMailSuppressed(string kind) =>
        MailSuppressed.Add(1, new KeyValuePair<string, object?>("kind", kind));

    public void RecordMail(string outcome) =>
        Mail.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    /// <summary>
    /// Reports how many messages wait for the relay as <c>wbskt_auth_mail_queue_depth</c>, read at each
    /// scrape. Called once, by the queue.
    /// </summary>
    public void ObserveMailQueueDepth(Func<int> depth) =>
        _meter.CreateObservableGauge("wbskt_auth_mail_queue_depth", depth);

    public void RecordBreachCheck(string outcome) =>
        BreachChecks.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordPasswordHash(double durationMs) => 
        PasswordHashDuration.Record(durationMs);

    public void RecordWorkerTick(string worker, string outcome) =>
        WorkerTicks.Add(1, new TagList { { "worker", worker }, { "outcome", outcome } });

    public void Dispose() => _meter.Dispose();
}
