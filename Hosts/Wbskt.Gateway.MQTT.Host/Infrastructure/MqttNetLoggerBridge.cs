using MQTTnet.Diagnostics.Logger;

namespace Wbskt.Gateway.MQTT.Host.Infrastructure;

/// <summary>MQTTnet's own log, into the host's, one level quieter: its errors are mostly one client's broken connection.</summary>
public sealed class MqttNetLoggerBridge : IMqttNetLogger
{
    private readonly ILogger _logger;

    public MqttNetLoggerBridge(ILogger<MqttNetLoggerBridge> logger)
    {
        _logger = logger;
    }

    public bool IsEnabled => _logger.IsEnabled(LogLevel.Debug);

    public void Publish(MqttNetLogLevel logLevel, string source, string message, object[]? parameters, Exception? exception)
    {
        var level = logLevel switch
        {
            MqttNetLogLevel.Error => LogLevel.Warning,
            MqttNetLogLevel.Warning => LogLevel.Information,
            _ => LogLevel.Debug
        };
        if (!_logger.IsEnabled(level))
        {
            return;
        }

        var text = parameters is { Length: > 0 } ? string.Format(System.Globalization.CultureInfo.InvariantCulture, message, parameters) : message;
        _logger.Log(level, exception, "MQTTnet {Source}: {Message}", source, text);
    }
}
