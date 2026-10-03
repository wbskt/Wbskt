using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.InboundAdapters;
using Wbskt.Workflow.Engine.Host.Providers;

namespace Wbskt.Workflow.Engine.Host.Tests.InboundAdapters;

internal sealed class RecordingHoldStateProvider(params TriggerRegistrationRow[] registrations) : IClientHoldStateProvider
{
    public List<(Guid ClientRefId, string MessageType, int WorkspaceId)> Lookups { get; } = [];
    public List<(string TriggerKey, bool Matches, DateTime EventAt, int HoldSeconds, string Payload)> Records { get; } = [];
    public List<ClientHoldStateRow> Due { get; init; } = [];
    public List<(int Id, DateTime SinceAt)> Fired { get; } = [];
    public List<int> Deleted { get; } = [];

    /// <summary>A recorder with no hold triggers registered, for tests that are not about holds.</summary>
    public static ClientHoldRecorder EmptyRecorder() => new(new RecordingHoldStateProvider(), new FixedFilterEvaluator(true));

    public Task<IReadOnlyCollection<TriggerRegistrationRow>> GetRegistrationsAsync(Guid clientRefId, string messageType, int workspaceId, CancellationToken ct)
    {
        Lookups.Add((clientRefId, messageType, workspaceId));
        return Task.FromResult<IReadOnlyCollection<TriggerRegistrationRow>>(registrations);
    }

    public Task RecordAsync(string triggerKey, bool matches, DateTime eventAt, int holdSeconds, string payload, CancellationToken ct)
    {
        Records.Add((triggerKey, matches, eventAt, holdSeconds, payload));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<ClientHoldStateRow>> LeaseDueAsync(int leaseSec, int batch, CancellationToken ct) =>
        Task.FromResult<IReadOnlyCollection<ClientHoldStateRow>>(Due);

    public Task MarkFiredAsync(int id, DateTime sinceAt, CancellationToken ct)
    {
        Fired.Add((id, sinceAt));
        return Task.CompletedTask;
    }

    public Task DeleteByIdAsync(int id, CancellationToken ct)
    {
        Deleted.Add(id);
        return Task.CompletedTask;
    }
}

internal sealed class FixedFilterEvaluator(bool passes) : ITriggerFilterEvaluator
{
    public Task<bool> PassesAsync(TriggerRegistrationRow registration, InboundEvent evt, CancellationToken ct) => Task.FromResult(passes);
}
