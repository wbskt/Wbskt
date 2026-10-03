using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.Providers;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

/// <summary>
/// Records a client message against every client trigger with a hold time that watches it: whether
/// the trigger's filter matched, and when. Nothing is dispatched here;
/// <see cref="HostedServices.ClientHoldTicker"/> starts the run once the filter has kept matching for
/// the trigger's whole hold time.
/// </summary>
public sealed class ClientHoldRecorder(IClientHoldStateProvider provider, ITriggerFilterEvaluator filterEvaluator)
{
    public async Task RecordAsync(InboundEvent message, Guid clientRefId, string messageType, int workspaceId, DateTime sentAtUtc, CancellationToken ct)
    {
        IReadOnlyCollection<TriggerRegistrationRow> registrations = await provider.GetRegistrationsAsync(clientRefId, messageType, workspaceId, ct);
        if (registrations.Count == 0)
        {
            return;
        }

        string payload = JsonSerializer.Serialize(message.Payload);
        foreach (TriggerRegistrationRow registration in registrations)
        {
            if (!ClientHoldTriggerKey.TryGetHoldSeconds(registration.TriggerKey, out int holdSeconds))
            {
                continue;
            }

            bool matches = await filterEvaluator.PassesAsync(registration, message, ct);
            await provider.RecordAsync(registration.TriggerKey, matches, sentAtUtc, holdSeconds, payload, ct);
        }
    }
}
