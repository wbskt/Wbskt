using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Engine.Host.Providers;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

/// <summary>
/// Parks a client's connect or disconnect against every presence trigger watching for it. Nothing
/// is dispatched here: <see cref="HostedServices.ClientPresenceTicker"/> picks each check up once
/// its trigger's grace period has passed and only then decides whether a run starts.
/// </summary>
public sealed class ClientPresenceParker(IClientPresenceCheckProvider provider)
{
    public async Task ParkAsync(Guid clientRefId, int workspaceId, ClientPresenceState state, DateTime changedAtUtc, CancellationToken ct)
    {
        // The event's own timestamp, unmodified: the management host writes the same value into the
        // client's presence columns, so the ticker can tell this change apart from a later one.
        DateTime changedAt = changedAtUtc;
        string stateName = ClientPresenceTriggerKey.StateName(state);
        IReadOnlyCollection<string> triggerKeys = await provider.GetTriggerKeysAsync(ClientPresenceTriggerKey.Prefix(clientRefId, state), workspaceId, ct);

        foreach (string triggerKey in triggerKeys)
        {
            if (!ClientPresenceTriggerKey.TryGetForSeconds(triggerKey, out int forSeconds))
            {
                continue;
            }

            await provider.InsertAsync(triggerKey, clientRefId, stateName, changedAt, changedAt.AddSeconds(forSeconds), ct);
        }
    }
}
