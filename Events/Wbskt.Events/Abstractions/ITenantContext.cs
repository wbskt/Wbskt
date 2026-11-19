using Wbskt.EventBus.Abstractions;

namespace Wbskt.Events.Abstractions;

/// <summary>An event about a whole tenant (its account), such as its pooled credits.</summary>
public interface ITenantContext : IEvent
{
    [SignalRPrivate] int TenantId { get; }
}
