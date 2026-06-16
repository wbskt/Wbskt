using MassTransit;
using Wbskt.Events.Workflow;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

public sealed class WorkflowRunCancellationRequestedEventConsumer : IConsumer<WorkflowRunCancellationRequestedEvent>
{
    private readonly IRunCancellationService _runCancellationService;

    public WorkflowRunCancellationRequestedEventConsumer(IRunCancellationService runCancellationService)
    {
        _runCancellationService = runCancellationService;
    }

    public Task Consume(ConsumeContext<WorkflowRunCancellationRequestedEvent> context)
    {
        _runCancellationService.CancelCts(context.Message.RunId);
        return Task.CompletedTask;
    }
}
