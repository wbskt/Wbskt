using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.Controllers;

/// <summary>What one matched trigger registration did with an inbound event.</summary>
public sealed record InboundDispatchEntry(long RegistrationId, Guid WorkflowRefId, string Outcome, Guid? RunRefId, long? RunId, string CorrelationKey);

internal static class InboundDispatchProjection
{
    /// <summary>
    /// Projects every per-registration outcome for the response. One inbound event can match several
    /// registrations, so a single run id would name one arbitrary winner and hide the rest.
    /// </summary>
    public static async Task<IReadOnlyList<InboundDispatchEntry>> ProjectAsync(TriggerDispatchResult result, IRunProvider runProvider, CancellationToken ct)
    {
        if (result.Registrations.Count == 0)
        {
            return [];
        }

        var entries = new InboundDispatchEntry[result.Registrations.Count];
        for (int i = 0; i < entries.Length; i++)
        {
            TriggerRegistrationDispatch dispatch = result.Registrations[i];
            Guid? runRefId = dispatch.RunId.HasValue ? (await runProvider.GetByIdAsync(dispatch.RunId.Value, ct)).RefId : null;
            entries[i] = new InboundDispatchEntry(dispatch.RegistrationId, dispatch.WorkflowRefId, dispatch.Outcome.ToString(), runRefId, dispatch.RunId, dispatch.CorrelationKey);
        }

        return entries;
    }
}
