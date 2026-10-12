using MassTransit;
using Wbskt.EventBus.Abstractions;

namespace Wbskt.EventBus.RabbitMQ;

public sealed class MassTransitCommandSender : ICommandSender
{
    private readonly IBus _bus;

    public MassTransitCommandSender(IBus bus)
    {
        _bus = bus;
    }

    public async Task SendAsync<TCommand>(TCommand command, CancellationToken ct = default) where TCommand : class, ICommand
    {
        var endpoint = await _bus.GetSendEndpoint(CommandQueue.Address<TCommand>());
        await endpoint.Send(command, ct);
    }
}
