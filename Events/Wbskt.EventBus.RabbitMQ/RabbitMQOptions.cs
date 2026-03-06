namespace Wbskt.EventBus.RabbitMQ;

public class RabbitMQOptions
{
    public string HostName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ExchangeName { get; set; } = "wbskt.events";
    public ushort PrefetchCount { get; set; } = 1;
}
