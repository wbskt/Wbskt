using MassTransit;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

public sealed class CancelWorkflowRunConsumerDefinition : ConsumerDefinition<CancelWorkflowRunConsumer>
{
    /// <summary>The shared queue every engine instance reads cancel commands from.</summary>
    public const string QueueName = "workflow-cancel-run";

    public CancelWorkflowRunConsumerDefinition()
    {
        // Name the shared queue explicitly to enable Competing Consumers: a command is handled once,
        // by whichever engine instance takes it, never once per instance. The queue is durable, so a
        // cancel sent while no engine is attached waits for one.
        EndpointName = QueueName;

        // A failed cancel (SQL briefly unavailable) is retried in place by the bus-wide policy
        // (ConsumerRetry.Default); handling is idempotent, so a retry after a partial attempt only
        // repeats cleanup. Past the last try the command moves to workflow-cancel-run_error.
    }
}
