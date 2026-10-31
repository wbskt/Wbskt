using MassTransit;
using Wbskt.Events.Client;

namespace Wbskt.Management.Host.Handlers;

public sealed class RequeueClientCommandConsumerDefinition : ConsumerDefinition<RequeueClientCommandConsumer>
{
    public RequeueClientCommandConsumerDefinition()
    {
        // One shared, durable queue: each hand-back is queued once, by whichever management instance
        // takes it, and one sent while none is attached waits. Queueing is idempotent on the command id,
        // so a retry after a partial attempt does not queue it twice.
        EndpointName = RequeueClientCommand.QueueName;
    }
}
