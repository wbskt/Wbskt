using MassTransit;
using Webskt.Management.Host.Providers;

namespace Webskt.Management.Host.Handlers;

internal sealed class ClientPresenceHandler : IConsumer<ClientConnectedEvent>, IConsumer<ClientDisconnectedEvent>
{
    private readonly IClientProvider _clientProvider;

    public ClientPresenceHandler(IClientProvider clientProvider)
    {
        _clientProvider = clientProvider;
    }

    public async Task Consume(ConsumeContext<ClientConnectedEvent> context)
    {
        var clientId = await _clientProvider.FindIdByRefIdAsync(context.Message.ClientRefId, context.CancellationToken);
        if (clientId > 0)
        {
            await _clientProvider.UpdatePresenceAsync(clientId, true, context.Message.CreatedAtUtc, context.CancellationToken);
        }
    }

    public async Task Consume(ConsumeContext<ClientDisconnectedEvent> context)
    {
        var clientId = await _clientProvider.FindIdByRefIdAsync(context.Message.ClientRefId, context.CancellationToken);
        if (clientId > 0)
        {
            await _clientProvider.UpdatePresenceAsync(clientId, false, context.Message.CreatedAtUtc, context.CancellationToken);
        }
    }
}
