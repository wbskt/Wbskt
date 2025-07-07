using System.Security.Authentication;
using System.Security.Claims;
using System.Security.Principal;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Wbskt.Common.Exceptions;

namespace Wbskt.Common.Extensions;

public static class AuthExtensions
{
    public static AuthenticationBuilder AddClientAuthScheme(this AuthenticationBuilder builder, IConfiguration configuration)
    {
        var key = configuration[Constants.JwtKeyNames.ClientServerTokenKey]!;
        builder.AddJwtBearer(Constants.AuthSchemes.ClientScheme, options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateLifetime = true,
                ValidateAudience = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
            };
        });

        return builder;
    }

    public static AuthenticationBuilder AddUserAuthScheme(this AuthenticationBuilder builder, IConfiguration configuration)
    {
        var key = configuration[Constants.JwtKeyNames.UserTokenKey]!;
        builder.AddJwtBearer(Constants.AuthSchemes.UserScheme, options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateLifetime = true,
                ValidateAudience = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
            };
        });

        return builder;
    }

    public static AuthenticationBuilder AddSocketServerAuthScheme(this AuthenticationBuilder builder, IConfiguration configuration)
    {
        var key = configuration[Constants.JwtKeyNames.SocketServerTokenKey]!;
        builder.AddJwtBearer(Constants.AuthSchemes.SocketServerScheme, options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateLifetime = true,
                ValidateAudience = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
            };
        });

        return builder;
    }

    public static AuthenticationBuilder AddCoreServerAuthScheme(this AuthenticationBuilder builder, IConfiguration configuration)
    {
        var key = configuration[Constants.JwtKeyNames.CoreServerTokenKey]!;
        builder.AddJwtBearer(Constants.AuthSchemes.CoreServerScheme, options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateLifetime = true,
                ValidateAudience = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
            };
        });

        return builder;
    }

    public static int GetUserId(this IPrincipal principal)
    {
        var claim = principal.GetClaim(Constants.Claims.UserData);
        return int.Parse(claim);
    }

    public static Guid GetClientUniqueId(this IPrincipal principal)
    {
        var claim = principal.GetClaim(Constants.Claims.ClientUniqueId);
        return Guid.Parse(claim);
    }

    public static Guid GetTokenId(this IPrincipal principal)
    {
        var claim = principal.GetClaim(Constants.Claims.TokenId);
        return Guid.Parse(claim);
    }

    public static Guid GetChannelChannelRef(this IPrincipal principal)
    {
        var claim = principal.GetClaim(Constants.Claims.ChannelRef);
        return Guid.Parse(claim);
    }

    public static string GetClientName(this IPrincipal principal)
    {
        return principal.GetClaim(Constants.Claims.ClientName);
    }

    public static HostString GetSocketServerAddress(this IEnumerable<Claim> claims)
    {
        var claim = claims.FirstOrDefault(c => c.Type == Constants.Claims.SocketServer);

        var addrString = claim!.Value.Split('|').Last();
        return new HostString(addrString);
    }

    public static Guid GetTokenId(this IEnumerable<Claim> claims)
    {
        var claim = claims.FirstOrDefault(c => c.Type == Constants.Claims.TokenId);

        return Guid.Parse(claim!.Value);
    }

    public static int GetSocketServerId(this IPrincipal principal)
    {
        var socketServer = principal.GetClaim(Constants.Claims.SocketServer);
        return int.Parse(socketServer.Split('|').First());
    }

    public static int GetClientId(this IPrincipal principal)
    {
        var clientId = principal.GetClaim(Constants.Claims.ClientId);
        return int.Parse(clientId);
    }

    public static int[] GetChannelIds(this IPrincipal principal)
    {
        var channelIds = principal.GetClaim(Constants.Claims.ChannelIds);
        return channelIds.Split(',').Select(int.Parse).ToArray();
    }

    private static string GetClaim(this IPrincipal principal, string claimKey)
    {
        if (principal.Identity is not ClaimsIdentity claimsPrincipal)
        {
            throw WbsktExceptions.UnableToGetClaim(claimKey);
        }

        var claim = claimsPrincipal.Claims.FirstOrDefault(c => c.Type.Equals(claimKey, StringComparison.InvariantCulture));
        if (claim == null)
        {
            throw WbsktExceptions.UnableToGetClaim(claimKey);
        }

        return claim.Value;
    }
}
