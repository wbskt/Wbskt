using MassTransit;
using Wbskt.Events.Client;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Protocol;

namespace Wbskt.Management.Host.Handlers;

/// <summary>
/// Keeps dbo.ClientMessageTypes current, so <c>GET clients/{ref}/message-types</c> is one indexed read
/// rather than a scan of the event log. Messages come in batches and are summed per client and type
/// first, so a chatty device costs one row write per batch, not one per message.
/// </summary>
public sealed class ClientMessageTypeRecorder : IConsumer<Batch<ClientMessageReceivedEvent>>
{
    internal const int MaxTypesPerClient = 100;
    internal const int MaxSamplePayloadChars = 4000;

    private readonly IClientMessageTypeProvider _provider;

    public ClientMessageTypeRecorder(IClientMessageTypeProvider provider)
    {
        _provider = provider;
    }

    public Task Consume(ConsumeContext<Batch<ClientMessageReceivedEvent>> context)
    {
        var tallies = Tally(context.Message.Select(m => m.Message));
        return tallies.Count == 0 ? Task.CompletedTask : _provider.RecordAsync(tallies, MaxTypesPerClient, context.CancellationToken);
    }

    internal static IReadOnlyCollection<ClientMessageTypeTally> Tally(IEnumerable<ClientMessageReceivedEvent> messages)
    {
        // Types compare ignoring case everywhere, the table's key included; the latest spelling wins.
        return messages
            .Where(m => DeviceMessageRules.IsValidType(m.Type))
            .GroupBy(m => (m.ClientId, Type: m.Type.ToLowerInvariant()))
            .Select(g =>
            {
                var latest = g.MaxBy(m => m.CreatedAtUtc)!;
                var sample = g.OrderByDescending(m => m.CreatedAtUtc).FirstOrDefault(m => m.Payload.Length <= MaxSamplePayloadChars)?.Payload;
                return new ClientMessageTypeTally(g.Key.ClientId, latest.Type, g.Count(), latest.CreatedAtUtc, sample);
            })
            .ToList();
    }
}

public sealed class ClientMessageTypeRecorderDefinition : ConsumerDefinition<ClientMessageTypeRecorder>
{
    private const int BatchSize = 100;

    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator, IConsumerConfigurator<ClientMessageTypeRecorder> consumerConfigurator, IRegistrationContext context)
    {
        // As the audit log: the broker must hand over a full batch at once, or every batch waits out the limit.
        endpointConfigurator.PrefetchCount = Math.Max(endpointConfigurator.PrefetchCount, BatchSize * 2);
        endpointConfigurator.ConcurrentMessageLimit = BatchSize * 2;
        consumerConfigurator.Options<BatchOptions>(o => o
            .SetMessageLimit(BatchSize)
            .SetTimeLimit(TimeSpan.FromSeconds(2))
            .SetConcurrencyLimit(1));
    }
}
