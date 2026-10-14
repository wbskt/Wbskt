using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Runtime;

/// <summary>
/// The trigger registrations last read for a <c>(channel, key)</c> lookup, kept so a device message
/// whose triggers have not changed is not another read of <c>dbo.TriggerRegistrations</c>. An empty
/// answer ("nothing listens for this") is kept like any other.
/// </summary>
/// <remarks>
/// Registrations change only in the management host, which announces each change with a
/// <c>WorkflowDefinitionChangedEvent</c>; the engine calls <see cref="Clear"/> when one arrives. That
/// drops every entry, in every workspace and empty ones included: the key alone does not say which
/// workspace it belongs to, and a change is rare next to device traffic. An announcement can be late
/// or lost (it is queued after the commit), so an entry also lives for a bounded time only
/// (<c>WorkflowEngineOptions.TriggerRegistrationCacheLifetime</c>).
/// </remarks>
public interface ITriggerRegistrationCache
{
    /// <summary>
    /// The cached registrations for the key, or the ones <paramref name="load"/> reads, which are then
    /// kept. For a caller whose action on a registration is checked again before anything starts.
    /// </summary>
    Task<IReadOnlyCollection<TriggerRegistrationRow>> GetOrLoadAsync(
        string channelKind,
        string key,
        Func<CancellationToken, Task<IReadOnlyCollection<TriggerRegistrationRow>>> load,
        CancellationToken ct);

    /// <summary>
    /// Answers from the cache only when the key is known to have no registrations; otherwise reads them
    /// with <paramref name="load"/> (and keeps that answer). For a caller that starts runs from what it
    /// gets back, so a registration removed a moment ago is never acted on from the cache.
    /// </summary>
    Task<IReadOnlyCollection<TriggerRegistrationRow>> LoadUnlessKnownEmptyAsync(
        string channelKind,
        string key,
        Func<CancellationToken, Task<IReadOnlyCollection<TriggerRegistrationRow>>> load,
        CancellationToken ct);

    /// <summary>Forgets every entry. A read already under way when this is called is not kept either.</summary>
    void Clear();
}
