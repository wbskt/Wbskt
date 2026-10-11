using System.Text.Json;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.Providers;
using Wbskt.Workflow.Channels;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

/// <summary>
/// Starts runs for client triggers with a hold time once their filter has kept matching for the whole
/// hold. The hold is then marked fired, so the trigger stays quiet until a message stops matching.
/// </summary>
/// <remarks>
/// The hold is judged on the messages that arrived. A device that stops sending while its last
/// reading matched is still holding when the time is up, and fires; a device going quiet is what the
/// presence trigger is for.
/// </remarks>
public sealed class ClientHoldTicker(
    IClock clock,
    ILeadershipGate leadership,
    IServiceScopeFactory scopeFactory,
    ILogger<ClientHoldTicker> logger,
    IOptions<WorkflowEngineOptions> options) : LeaderPeriodicWorker(leadership, scopeFactory, logger)
{
    internal const int LeaseSeconds = 15;

    private readonly int _batchSize = options.Value.ClientHoldLeaseBatchSize;

    protected override string Name => "Client hold ticker";

    protected override TimeSpan Interval { get; } = options.Value.ClientHoldTickInterval;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var provider = services.GetRequiredService<IClientHoldStateProvider>();
        var inboundHub = services.GetRequiredService<IInboundHub>();
        IReadOnlyCollection<ClientHoldStateRow> holds = await provider.LeaseDueAsync(LeaseSeconds, _batchSize, ct);
        DateTime now = clock.UtcNow;

        foreach (ClientHoldStateRow hold in holds)
        {
            try
            {
                Logger.LogInformation("Client trigger {TriggerKey} held since {SinceAt}; dispatching", hold.TriggerKey, hold.SinceAt);
                TriggerDispatchResult result = await inboundHub.HandleAsync(ToInboundEvent(hold, now), ct);

                if (result.Outcome == TriggerDispatchOutcome.NoRegistration)
                {
                    // The trigger was removed (its workflow disabled, or republished without it) while
                    // this hold was running. Nothing will ever clear the row, so it goes now.
                    Logger.LogDebug("Client trigger {TriggerKey} is no longer registered; dropping its hold state", hold.TriggerKey);
                    await provider.DeleteByIdAsync(hold.Id, ct);
                    continue;
                }

                await provider.MarkFiredAsync(hold.Id, hold.SinceAt, ct);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to process client hold {HoldId}", hold.Id);
            }
        }
    }

    internal static InboundEvent ToInboundEvent(ClientHoldStateRow hold, DateTime now)
    {
        ClientHoldTriggerKey.TryGetHoldSeconds(hold.TriggerKey, out int holdSeconds);

        // The latest matching message, exactly as a plain client trigger would have seen it, plus when
        // the hold began and how long it had to last.
        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(hold.Payload) ?? [];
        payload["heldSince"] = JsonSerializer.SerializeToElement(DateTime.SpecifyKind(hold.SinceAt, DateTimeKind.Utc));
        payload["holdSeconds"] = JsonSerializer.SerializeToElement(holdSeconds);

        return new InboundEvent(
            ClientHoldChannel.Name,
            [hold.TriggerKey],
            // One id per hold: a re-leased hold (the process died before it was marked fired) is dropped
            // by the dispatcher's idempotency claim, while a later hold of the same trigger is not.
            $"client-hold:{hold.Id}:{hold.SinceAt.Ticks}",
            payload,
            now);
    }
}
