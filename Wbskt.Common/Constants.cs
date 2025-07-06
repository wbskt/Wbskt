namespace Wbskt.Common;

public static class Constants
{
    public static class Application
    {
        public const string AppFolderName = "Wbskt";
    }
    public static class JwtKeyNames
    {
        public const string UserTokenKey = "Jwt:UserTokenKey";
        public const string CoreServerTokenKey = "Jwt:CoreServerTokenKey";
        public const string SocketServerTokenKey = "Jwt:SocketServerTokenKey";
        public const string ClientServerTokenKey = "Jwt:ClientServerTokenKey";
    }

    public static class AuthSchemes
    {
        public const string UserScheme = "Bearer";
        public const string ClientScheme = "Client";
        public const string SocketServerScheme = "Server";
        public const string CoreServerScheme = "Core";
    }

    public static class Claims
    {
        public const string Name = "Name";
        public const string EmailId = "Email";
        public const string TokenId = "TokenId";
        public const string UserData = "UserData";
        public const string ClientId = "ClientId";
        public const string ChannelRef = "ChannelRef";
        public const string CoreServer = "CoreServer";
        public const string ChannelIds = "ChannelIds";
        public const string ClientName = "ClientName";
        public const string SocketServer = "SocketServer";
        public const string ClientUniqueId = "ClientUniqueId";
    }

    public static class ExpiryTimes // in minutes
    {
        public const int ClientTokenExpiry = 1; // 1 minute
        public const int ServerTokenExpiry = 60 * 24; // one day
    }

    public static class LoggingConstants
    {
        public const string LogPath = "LogPath";
        public const string LogTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";
    }

    public enum ServerType
    {
        CoreServer,
        SocketServer
    }
}
