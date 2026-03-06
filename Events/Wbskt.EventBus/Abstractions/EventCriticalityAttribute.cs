namespace Wbskt.EventBus.Abstractions;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class EventCriticalityAttribute : Attribute
{
    public EventCriticality Criticality { get; }

    public EventCriticalityAttribute(EventCriticality criticality)
    {
        Criticality = criticality;
    }
}
