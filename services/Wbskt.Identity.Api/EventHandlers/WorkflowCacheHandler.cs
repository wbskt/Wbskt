using Wbskt.Common.Events;
using Wbskt.Common.Services;
using Wbskt.EventBus;

namespace Wbskt.Identity.Api.EventHandlers;

public class WorkflowCacheHandler : IEventHandler<WorkflowCreatedEvent>,
    IEventHandler<WorkflowUpdatedEvent>,
    IEventHandler<WorkflowDeletedEvent>
{
    private readonly ICacheService _cacheService;

    public WorkflowCacheHandler(ICacheService cacheService)
    {
        _cacheService = cacheService;
    }

    public Task HandleAsync(WorkflowCreatedEvent @event, CancellationToken cancellationToken)
    {
        var cacheKey = $"Workflows_User_{@event.UserId}";
        _cacheService.Remove(cacheKey);
        return Task.CompletedTask;
    }

    public Task HandleAsync(WorkflowUpdatedEvent @event, CancellationToken cancellationToken)
    {
        var cacheKey = $"Workflows_User_{@event.UserId}";
        _cacheService.Remove(cacheKey);
        return Task.CompletedTask;
    }

    public Task HandleAsync(WorkflowDeletedEvent @event, CancellationToken cancellationToken)
    {
        var cacheKey = $"Workflows_User_{@event.UserId}";
        _cacheService.Remove(cacheKey);
        return Task.CompletedTask;
    }
}
