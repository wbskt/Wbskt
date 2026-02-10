using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Webskt.Common.Abstraction.Events;
using Webskt.Common.Events;

namespace Webskt.Common.Infrastructure.RabbitMQ;

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

            // Set QoS (Prefetch Count)
            await _channel.BasicQosAsync(0, _options.PrefetchCount, false, stoppingToken);

            // Every instance gets its own queue (for broadcast support)
            var queueName = await _channel.QueueDeclareAsync(
                queue: string.Empty, 
                durable: false, 
                exclusive: true, 
                autoDelete: true, 
                cancellationToken: stoppingToken);

            // Bind to all events (# matches any routing key)
            await _channel.QueueBindAsync(queueName.QueueName, _options.ExchangeName, "#", cancellationToken: stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += async (model, ea) =>
            {
                try
                {
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);
                    var routingKey = ea.RoutingKey;

                    // 1. Resolve actual Event Type (O(1) lookup)
                    var eventType = _resolver.GetEventType(routingKey);
                    if (eventType == null) 
                    {
                        return;
                    }

                    var @event = JsonSerializer.Deserialize(message, eventType) as IEvent;
                    if (@event == null) return;

                    // 2. Resolve Handlers
                    var handlerTypes = _resolver.GetHandlerTypes(eventType);

                    // 3. Execute each handler in a NEW SCOPE
                    foreach (var handlerType in handlerTypes)
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var handlerService = scope.ServiceProvider.GetRequiredService(handlerType);

                        /* 
                           NOTE FOR FUTURE TESTING:
                           The following cast will return NULL for specific handlers (e.g., IEventHandler<DeviceCommandEvent>).
                           Even though DeviceCommandEvent is an IEvent, C# contravariance (the 'in' keyword) does not 
                           allow casting a specific handler to a general one to prevent type-safety violations 
                           (e.g., accidentally passing a UserEvent into a DeviceCommandHandler).
                           
                           var handler = handlerService as IEventHandler<IEvent>;
                           if (handler == null) { // This will be true for specific handlers }
                        */

                        // We use reflection to call the generic HandleAsync method because we have already
                        // manually verified the type compatibility in the EventHandlerResolver.
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
