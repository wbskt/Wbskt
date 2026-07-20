using MassTransit;
using Wbskt.Events.Client;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Management.Host.Handlers;

// RTT is measured by the socket host (which owns the connection); this side only persists it.
public sealed class ClientLatencyMeasuredHandler : IConsumer<ClientLatencyMeasuredEvent>
{
    private readonly IClientProvider _clientProvider;

    public ClientLatencyMeasuredHandler(IClientProvider clientProvider)
    {
        _clientProvider = clientProvider;
    }

    public async Task Consume(ConsumeContext<ClientLatencyMeasuredEvent> context)
    {
        var message = context.Message;
        var roundTripMs = (int)Math.Clamp(Math.Round(message.RoundTripMs), 0, int.MaxValue);
        await _clientProvider.UpdateRttAsync(message.ClientId, roundTripMs, message.CreatedAtUtc, context.CancellationToken);
    }
}
