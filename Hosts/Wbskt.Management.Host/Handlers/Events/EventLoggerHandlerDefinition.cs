using MassTransit;
using Microsoft.Extensions.Options;
using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Handlers.Events;

public sealed class EventLoggerHandlerDefinition : ConsumerDefinition<EventLoggerHandler>
{
    private readonly EventLoggingOptions _options;

    public EventLoggerHandlerDefinition(IOptions<EventLoggingOptions> options)
    {
        _options = options.Value;

        // Name the shared queue explicitly to enable Competing Consumers
        EndpointName = "event-audit-log";
    }

    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator, IConsumerConfigurator<EventLoggerHandler> consumerConfigurator, IRegistrationContext context)
    {
        // A batch is delivered when it is full or when the oldest message in it has waited the time
        // limit, whichever comes first. The broker must hand over at least a full batch at once, or
        // every batch would wait out the time limit.
        int batchSize = Math.Max(1, _options.BatchSize);
        endpointConfigurator.PrefetchCount = Math.Max(endpointConfigurator.PrefetchCount, batchSize * 2);
        endpointConfigurator.ConcurrentMessageLimit = batchSize * 2;
        consumerConfigurator.Options<BatchOptions>(o => o
            .SetMessageLimit(batchSize)
            .SetTimeLimit(TimeSpan.FromSeconds(Math.Max(1, _options.FlushIntervalLimitInSeconds)))
            .SetConcurrencyLimit(1));

        // A failed insert (SQL down, a deploy restarting it) retries the whole batch, backing off to
        // two minutes between tries, so a short outage just delays the log. A longer one moves the
        // batch to the event-audit-log_error queue, where it waits to be moved back, not dropped.
        endpointConfigurator.UseMessageRetry(r => r.Exponential(10, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(2)));
    }
}
