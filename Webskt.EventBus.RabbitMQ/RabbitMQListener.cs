using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Webskt.EventBus.Abstractions;
using Webskt.EventBus.Implementations;

namespace Webskt.EventBus.RabbitMQ;

public sealed class RabbitMQListener : BackgroundService
{
    private readonly RabbitMQOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly EventHandlerResolver _resolver;
    private readonly ILogger<RabbitMQListener> _logger;
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMQListener(
        IOptions<RabbitMQOptions> options,
        IServiceProvider serviceProvider,
        EventHandlerResolver resolver,
        ILogger<RabbitMQListener> logger)
    {
        _options = options.Value;
        _serviceProvider = serviceProvider;
        _resolver = resolver;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            UserName = _options.UserName,
            Password = _options.Password
        };

        try 
        {
            _connection = await factory.CreateConnectionAsync(stoppingToken);
            _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

            await _channel.ExchangeDeclareAsync(_options.ExchangeName, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);

            await _channel.BasicQosAsync(0, _options.PrefetchCount, false, stoppingToken);

            var queueName = await _channel.QueueDeclareAsync(
                queue: string.Empty, 
                durable: false, 
                exclusive: true, 
                autoDelete: true, 
                cancellationToken: stoppingToken);

            await _channel.QueueBindAsync(queueName.QueueName, _options.ExchangeName, "#", cancellationToken: stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += async (_, ea) =>
            {
                try
                {
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);
                    var routingKey = ea.RoutingKey;

                    var eventType = _resolver.GetEventType(routingKey);
                    if (eventType == null) 
                    {
                        return;
                    }

                    var handlerTypes = _resolver.GetHandlerTypes(eventType);

                    var @event = JsonSerializer.Deserialize(message, eventType) as IEvent;
                    if (@event == null) return;

                    foreach (var handlerType in handlerTypes)
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var handlerService = scope.ServiceProvider.GetRequiredService(handlerType);

                        var method = handlerType.GetMethod(nameof(IEventHandler<>.HandleAsync));
                        if (method != null)
                        {
                            await (Task)method.Invoke(handlerService, [@event, stoppingToken])!;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing event from routing key: {RoutingKey}", ea.RoutingKey);
                }
            };

            await _channel.BasicConsumeAsync(queueName.QueueName, autoAck: true, consumer: consumer, cancellationToken: stoppingToken);

            _logger.LogInformation("RabbitMQ Listener is active and bound to {ExchangeName}", _options.ExchangeName);
            
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "RabbitMQ Listener failed to start.");
        }
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}
