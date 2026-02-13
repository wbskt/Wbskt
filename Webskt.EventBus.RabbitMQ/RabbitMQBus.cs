using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Webskt.EventBus.Abstractions;

namespace Webskt.EventBus.RabbitMQ;

public sealed class RabbitMQBus : IEventBus, IDisposable
{
    private readonly RabbitMQOptions _options;
    private IConnection? _connection;
    private IChannel? _channel;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    public RabbitMQBus(IOptions<RabbitMQOptions> options)
    {
        _options = options.Value;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await EnsureConnectionAsync();
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
    {
        await EnsureConnectionAsync();

        var json = JsonSerializer.Serialize(@event, @event.GetType());
        var body = Encoding.UTF8.GetBytes(json);

        var routingKey = @event.GetType().FullName ?? @event.GetType().Name;

        await _channel!.BasicPublishAsync(
            exchange: _options.ExchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: new BasicProperties(),
            body: body,
            cancellationToken: ct);
    }

    private async Task EnsureConnectionAsync()
    {
        if (_channel is { IsOpen: true }) return;

        await _connectionLock.WaitAsync();
        try
        {
            if (_channel is { IsOpen: true }) return;

            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                UserName = _options.UserName,
                Password = _options.Password
            };

            _connection = await factory.CreateConnectionAsync();
            _channel = await _connection.CreateChannelAsync();

            await _channel.ExchangeDeclareAsync(
                exchange: _options.ExchangeName, 
                type: ExchangeType.Topic, 
                durable: true);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}
