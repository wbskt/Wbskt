using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Channels;

public sealed class TriggerChannelRegistry : ITriggerChannelRegistry
{
    /// <summary>Every channel the engine has. Channels hold no state, so one set serves every caller.</summary>
    public static TriggerChannelRegistry Default { get; } = new(
    [
        new ClientChannel(),
        new ClientHoldChannel(),
        new PresenceChannel(),
        new ScheduleChannel(),
        new WebhookChannel(),
        new ManualChannel(),
        new SignalChannel(),
        new HttpWakeChannel(),
        new ChildCompletedChannel()
    ]);

    private readonly Dictionary<string, ITriggerChannel> _byKind;

    public TriggerChannelRegistry(IReadOnlyCollection<ITriggerChannel> channels)
    {
        All = channels;
        _byKind = channels.ToDictionary(channel => channel.Kind, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<ITriggerChannel> All { get; }

    public ITriggerChannel? Find(string kind) => _byKind.GetValueOrDefault(kind);
}
