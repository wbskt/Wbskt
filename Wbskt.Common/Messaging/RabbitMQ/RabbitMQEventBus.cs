using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Wbskt.EventBus;

namespace Wbskt.Common.Messaging.RabbitMQ;

public class RabbitMQEventBus : IEventBus, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConnection _connection;
    private readonly IModel _channel;

    public RabbitMQEventBus(IServiceProvider serviceProvider, string hostname)
    {
        _serviceProvider = serviceProvider;
        var factory = new ConnectionFactory() { HostName = hostname };
        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
        _channel.ExchangeDeclare(exchange: "wbskt_exchange", type: ExchangeType.Topic);
    }

    public Task PublishAsync(IEvent @event, CancellationToken cancellationToken)
    {
        var eventName = @event.GetType().Name;
        var message = JsonSerializer.Serialize(@event, @event.GetType());
        var body = Encoding.UTF8.GetBytes(message);

        _channel.BasicPublish(exchange: "wbskt_exchange",
                             routingKey: eventName,
                             basicProperties: null,
                             body: body);

        return Task.CompletedTask;
    }

    public void Subscribe<TEvent, TEventHandler>()
        where TEvent : IEvent
        where TEventHandler : IEventHandler<TEvent>
    {
        var eventName = typeof(TEvent).Name;
        var queueName = _channel.QueueDeclare().QueueName;
        _channel.QueueBind(queue: queueName, exchange: "wbskt_exchange", routingKey: eventName);

        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += async (_, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            var @event = JsonSerializer.Deserialize<TEvent>(message);

            if (@event != null)
            {
                using var scope = _serviceProvider.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<TEventHandler>();
                await handler.HandleAsync(@event, CancellationToken.None);
            }
        };

        _channel.BasicConsume(queue: queueName, autoAck: true, consumer: consumer);
    }

    public void Dispose()
    {
        _channel.Close();
        _connection.Close();
    }
}
