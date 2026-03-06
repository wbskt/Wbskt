namespace Webskt.Foundation.Abstraction.Constants;

public static class Logging
{
    public const string LogPath = "LogPath";
    public const string LogName = "LogName";
    public const string LogTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";
}