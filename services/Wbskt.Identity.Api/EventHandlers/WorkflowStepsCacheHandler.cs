using Wbskt.Common.Events;
using Wbskt.Common.Services;
using Wbskt.EventBus;

namespace Wbskt.Identity.Api.EventHandlers;

public class WorkflowStepsCacheHandler : IEventHandler<WorkflowStepsChangedEvent>
{
    private readonly ICacheService _cacheService;

    public WorkflowStepsCacheHandler(ICacheService cacheService)
    {
        _cacheService = cacheService;
    }

    public Task HandleAsync(WorkflowStepsChangedEvent @event, CancellationToken cancellationToken)
    {
        var cacheKey = $"WorkflowSteps_{@event.WorkflowId}";
        _cacheService.Remove(cacheKey);
        return Task.CompletedTask;
    }
}
