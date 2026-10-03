using System.Text.Json;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.Providers;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

/// <summary>
/// Starts presence-trigger runs once a parked connect or disconnect has outlasted its trigger's
/// grace period. Each due check is compared against the client's presence as it stands now: a
/// client that came back (or dropped again) in the meantime makes the check stale, and it is
/// deleted without starting anything.
/// </summary>
public sealed class ClientPresenceTicker : BackgroundService
{
    private const string LeaseName = "presence-tick";

    // Short on purpose: a check whose client presence has not caught up yet is simply left leased,
    // and is looked at again once this lease runs out.
    internal const int LeaseSeconds = 15;

    // How long a check may wait for the client's presence to catch up before it is given up on.
    // It also bounds the one case that never resolves: a disconnect reported by a socket host the
    // client had already moved off (the connection was superseded), while the client stays online.
    internal static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(10);

    // Presence timestamps go through the database's datetime types on both sides; anything closer
    // than this is the same moment.
    private static readonly TimeSpan SameMoment = TimeSpan.FromMilliseconds(10);

    private readonly IClock _clock;
    private readonly ILeaseHolder _leaseHolder;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ClientPresenceTicker> _logger;
    private readonly int _batchSize;
    private readonly TimeSpan _pollInterval;

    public ClientPresenceTicker(
        IClock clock,
        ILeaseHolder leaseHolder,
        IServiceScopeFactory scopeFactory,
        ILogger<ClientPresenceTicker> logger,
        IOptions<WorkflowEngineOptions> options)
    {
        _clock = clock;
        _leaseHolder = leaseHolder;
        _scopeFactory = scopeFactory;
        _logger = logger;
        // Same cadence and batch as the schedule ticker: both turn a due row into an inbound event.
        _batchSize = options.Value.ScheduledFireLeaseBatchSize;
        _pollInterval = options.Value.ScheduleTickInterval;
    }

    public async Task ProcessDueChecksAsync(CancellationToken ct)
    {
        if (!await _leaseHolder.IsHeldAsync(LeaseName, ct))
        {
            return;
        }

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<IClientPresenceCheckProvider>();
        var inboundHub = scope.ServiceProvider.GetRequiredService<IInboundHub>();
        IReadOnlyCollection<ClientPresenceCheckRow> checks = await provider.LeaseDueAsync(LeaseSeconds, _batchSize, ct);
        DateTime now = _clock.UtcNow;

        foreach (ClientPresenceCheckRow check in checks)
        {
            try
            {
                switch (Evaluate(check, now))
                {
                    case PresenceCheckVerdict.Pending:
                        _logger.LogDebug("Presence check {CheckId} is waiting for client {ClientRefId}'s presence to catch up", check.Id, check.ClientRefId);
                        continue;

                    case PresenceCheckVerdict.Fire:
                        _logger.LogInformation("Client {ClientRefId} stayed {State}; dispatching presence trigger {TriggerKey}", check.ClientRefId, check.State, check.TriggerKey);
                        await inboundHub.HandleAsync(ToInboundEvent(check, now), ct);
                        break;

                    case PresenceCheckVerdict.Stale:
                        _logger.LogDebug("Presence check {CheckId} is stale; client {ClientRefId} did not stay {State}", check.Id, check.ClientRefId, check.State);
                        break;
                }

                await provider.DeleteByIdAsync(check.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process presence check {CheckId}", check.Id);
            }
        }
    }

    internal static PresenceCheckVerdict Evaluate(ClientPresenceCheckRow check, DateTime now)
    {
        if (check.ClientId is null)
        {
            return PresenceCheckVerdict.Stale;
        }

        PresenceCheckVerdict verdict = check.State == ClientPresenceTriggerKey.StateName(ClientPresenceState.Online)
            ? EvaluateOnline(check)
            : EvaluateOffline(check);

        return verdict == PresenceCheckVerdict.Pending && now - check.DueAt > GiveUpAfter
            ? PresenceCheckVerdict.Stale
            : verdict;
    }

    // The management host records presence from the same events this engine parks, but on its own
    // consumer, so it can lag behind. "Pending" is that lag: the client row does not show this
    // change, or anything after it, yet.
    private static PresenceCheckVerdict EvaluateOffline(ClientPresenceCheckRow check)
    {
        if (check.ClientIsConnected == true)
        {
            // Connected since the disconnect: it came back within the grace period.
            return check.ClientConnectedAt is { } connectedAt && connectedAt - check.ChangedAt > SameMoment
                ? PresenceCheckVerdict.Stale
                : PresenceCheckVerdict.Pending;
        }

        // Disconnected, but by a later disconnect: it came back and dropped again, and that later
        // disconnect has a check of its own.
        return check.ClientLastActivityAt is { } lastActivity && lastActivity - check.ChangedAt > SameMoment
            ? PresenceCheckVerdict.Stale
            : PresenceCheckVerdict.Fire;
    }

    private static PresenceCheckVerdict EvaluateOnline(ClientPresenceCheckRow check)
    {
        if (check.ClientIsConnected == true && check.ClientConnectedAt is { } connectedAt)
        {
            TimeSpan sinceChange = connectedAt - check.ChangedAt;
            if (sinceChange.Duration() <= SameMoment)
            {
                return PresenceCheckVerdict.Fire;
            }

            // A later connection has its own check; an earlier one means the row has not caught up.
            return sinceChange > TimeSpan.Zero ? PresenceCheckVerdict.Stale : PresenceCheckVerdict.Pending;
        }

        // Disconnected after this connect: it did not stay online.
        return check.ClientLastActivityAt is { } lastActivity && lastActivity - check.ChangedAt >= -SameMoment
            ? PresenceCheckVerdict.Stale
            : PresenceCheckVerdict.Pending;
    }

    internal static InboundEvent ToInboundEvent(ClientPresenceCheckRow check, DateTime now)
    {
        ClientPresenceTriggerKey.TryGetForSeconds(check.TriggerKey, out int forSeconds);
        var payload = new Dictionary<string, JsonElement>
        {
            ["clientRefId"] = JsonSerializer.SerializeToElement(check.ClientRefId),
            ["clientId"] = JsonSerializer.SerializeToElement(check.ClientId),
            ["workspaceId"] = JsonSerializer.SerializeToElement(check.ClientWorkspaceId),
            ["state"] = JsonSerializer.SerializeToElement(check.State),
            ["changedAt"] = JsonSerializer.SerializeToElement(DateTime.SpecifyKind(check.ChangedAt, DateTimeKind.Utc)),
            ["forSeconds"] = JsonSerializer.SerializeToElement(forSeconds)
        };

        return new InboundEvent(
            ClientPresenceTriggerKey.TriggerKind,
            [check.TriggerKey],
            // The check's own id: stable if the process dies between this dispatch and the delete, so
            // the re-leased check is dropped by the dispatcher's idempotency claim instead of running twice.
            $"presence:{check.Id}",
            payload,
            now);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Client presence ticker is starting.");
        using var timer = new PeriodicTimer(_pollInterval);
        try
        {
            do
            {
                try
                {
                    await ProcessDueChecksAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Client presence ticker tick failed.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Client presence ticker is stopping.");
        }
    }
}

internal enum PresenceCheckVerdict
{
    Fire,
    Stale,
    Pending
}
