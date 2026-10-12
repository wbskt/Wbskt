using System.Text.Json;
using MassTransit;

namespace Wbskt.EventBus.RabbitMQ;

/// <summary>The retry policy every consumer gets unless its definition sets its own.</summary>
public static class ConsumerRetry
{
    /// <summary>
    /// Five tries over about a minute: long enough to ride out a SQL failover or a deploy restarting a
    /// dependency, short enough that a message that keeps failing reaches its _error queue (and the
    /// dead-letter alert) promptly. A message that can never succeed is not retried at all.
    /// </summary>
    public static void Default(IRetryConfigurator retry)
    {
        retry.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2));

        // Malformed or invalid input fails the same way every time.
        retry.Ignore<ArgumentException>();
        retry.Ignore<JsonException>();
        retry.Ignore<FormatException>();
    }
}
