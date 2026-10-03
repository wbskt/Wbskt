using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Engine.Host.Providers;

public interface IClientHoldStateProvider
{
    /// <summary>
    /// Hold registrations for this client's message type, and its any-type ones, on enabled workflows
    /// in the workspace.
    /// </summary>
    Task<IReadOnlyCollection<TriggerRegistrationRow>> GetRegistrationsAsync(Guid clientRefId, string messageType, int workspaceId, CancellationToken ct);

    /// <summary>Records one message against one hold trigger; see <c>dbo.ClientHoldState_Record</c>.</summary>
    Task RecordAsync(string triggerKey, bool matches, DateTime eventAt, int holdSeconds, string payload, CancellationToken ct);

    Task<IReadOnlyCollection<ClientHoldStateRow>> LeaseDueAsync(int leaseSec, int batch, CancellationToken ct);

    Task MarkFiredAsync(int id, DateTime sinceAt, CancellationToken ct);

    Task DeleteByIdAsync(int id, CancellationToken ct);
}
