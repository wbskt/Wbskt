using MassTransit;
using Wbskt.Events.Client;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Management.Host.Handlers;

public sealed class ClientPresenceHandler : IConsumer<ClientConnectedEvent>, IConsumer<ClientDisconnectedEvent>
{
    private readonly IClientProvider _clientProvider;

    public ClientPresenceHandler(IClientProvider clientProvider)
    {
        _clientProvider = clientProvider;
    }

    public async Task Consume(ConsumeContext<ClientConnectedEvent> context)
    {
        var message = context.Message;
        await _clientProvider.UpdatePresenceAsync(message.ClientId, true, message.CreatedAtUtc, message.HostId, context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ClientDisconnectedEvent> context)
    {
        var message = context.Message;
        await _clientProvider.UpdatePresenceAsync(message.ClientId, false, message.CreatedAtUtc, message.HostId, context.CancellationToken);
    }
}
