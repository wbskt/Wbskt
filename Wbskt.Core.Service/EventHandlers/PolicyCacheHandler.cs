using Wbskt.Common.Events;
using Wbskt.Common.Services;
using Wbskt.EventBus;

namespace Wbskt.Core.Service.EventHandlers;

public class PolicyCacheHandler : IEventHandler<PolicyCreatedEvent>,
    IEventHandler<PolicyUpdatedEvent>,
    IEventHandler<PolicyDeletedEvent>
{
    private readonly ICacheService _cacheService;

    public PolicyCacheHandler(ICacheService cacheService)
    {
        _cacheService = cacheService;
    }

    public Task HandleAsync(PolicyCreatedEvent @event, CancellationToken cancellationToken)
    {
        var cacheKey = $"Policies_User_{@event.UserId}";
        _cacheService.Remove(cacheKey);
        return Task.CompletedTask;
    }

    public Task HandleAsync(PolicyUpdatedEvent @event, CancellationToken cancellationToken)
    {
        var cacheKey = $"Policies_User_{@event.UserId}";
        _cacheService.Remove(cacheKey);
        return Task.CompletedTask;
    }

    public Task HandleAsync(PolicyDeletedEvent @event, CancellationToken cancellationToken)
    {
        var cacheKey = $"Policies_User_{@event.UserId}";
        _cacheService.Remove(cacheKey);
        return Task.CompletedTask;
    }
}
