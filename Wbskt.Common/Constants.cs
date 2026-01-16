namespace Wbskt.Common;

public static class Constants
{
    public enum ServerType
    {
        CoreServer,
        SocketServer
    }

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
        public const string Issuer = "Jwt:Issuer";
        public const string Audience = "Jwt:Audience";
    }

    public static class AuthSchemes
    {
        public const string UserScheme = "Bearer";
        public const string ClientScheme = "Client";
        public const string SocketServerScheme = "Server";
        public const string CoreServerScheme = "Core";
        public const string AuthServerScheme = "WbsktAuth";
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
        public static TimeSpan CacheExpiry = TimeSpan.FromMinutes(5);
    }

    public static class LoggingConstants
    {
        public const string LogPath = "LogPath";
        public const string LogName = "LogName";
        public const string LogTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";
    }

    public static class Clients
    {
        public const string Postman = "postman";
        public const string WbsktFrontend = "wbskt-frontend";
    }

    public static class Permissions
    {
        public const string PolicyRead = "policy.read";
        public const string PolicyWrite = "policy.write";
        public const string PolicyDelete = "policy.delete";
        public const string WorkflowRead = "workflow.read";
        public const string WorkflowWrite = "workflow.write";
        public const string WorkflowDelete = "workflow.delete";
        public const string ClientRead = "client.read";
        public const string ClientWrite = "client.write";
        public const string ClientDelete = "client.delete";
        public const string Permission = "permission";
        public const string AspNetIdentitySecurityStamp = "AspNet.Identity.SecurityStamp";
    }

    public static class Roles
    {
        public const string Admin = "Admin";
        public const string User = "User";
    }

    public static class Audiences
    {
        public const string WbsktApi = "wbskt_api";
    }
}
