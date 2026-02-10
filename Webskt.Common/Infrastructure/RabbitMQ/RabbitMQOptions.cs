namespace Webskt.Common.Infrastructure.RabbitMQ;

public sealed class RabbitMQOptions
{
    public string HostName { get; set; } = "localhost";
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string ExchangeName { get; set; } = "wbskt.events";
    public ushort PrefetchCount { get; set; } = 50;
}
