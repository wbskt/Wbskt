using System.Globalization;
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
/// <remarks>
/// The hold registrations are taken from <see cref="ITriggerRegistrationCache"/>, registrations and
/// "none" alike, rather than read for every message. A registration removed a moment ago can still be
/// recorded against until the change is announced; that starts nothing, because the ticker dispatches
/// a due hold through the trigger dispatcher, which reads the registration again and drops the hold
/// when it is gone.
/// </remarks>
public sealed class ClientHoldRecorder(IClientHoldStateProvider provider, ITriggerFilterEvaluator filterEvaluator, ITriggerRegistrationCache registrationCache)
{
    public async Task RecordAsync(InboundEvent message, Guid clientRefId, string messageType, int workspaceId, DateTime sentAtUtc, CancellationToken ct)
    {
        // The workspace is part of the key because the lookup is scoped to it.
        string key = string.Create(CultureInfo.InvariantCulture, $"{workspaceId}:{ClientHoldTriggerKey.Prefix(clientRefId, messageType)}");
        IReadOnlyCollection<TriggerRegistrationRow> registrations = await registrationCache.GetOrLoadAsync(
            ClientHoldTriggerKey.TriggerKind,
            key,
            token => provider.GetRegistrationsAsync(clientRefId, messageType, workspaceId, token),
            ct);
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
