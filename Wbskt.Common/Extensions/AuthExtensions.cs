using System.Security.Claims;
using System.Security.Principal;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Wbskt.Common.Exceptions;

namespace Wbskt.Common.Extensions;

public static class AuthExtensions
{
    public static AuthenticationBuilder AddWbsktAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        return services.AddAuthentication(options =>
        {
            // Default to the Auth Server (Users & Internal Services)
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            // OIDC / OpenIddict Configuration for Users and Internal Servers
            var authority = configuration["AuthServer:Authority"];
            options.Authority = authority;
            options.Audience = configuration["AuthServer:Audience"];
            options.RequireHttpsMetadata = false; // Set to true in production
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = authority, // Explicitly set the issuer
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true
            };
        })
        .AddJwtBearer(Constants.AuthSchemes.ClientScheme, options =>
        {
            // specialized Scheme for IoT Devices (Symmetric Key)
            var key = configuration[Constants.JwtKeyNames.ClientServerTokenKey];
            if (string.IsNullOrEmpty(key))
            {
                // If no key is configured (e.g. Auth Service doesn't need this), we can skip or warn.
                // For now, we allow it to be null but validation will fail if used.
                return;
            }

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateLifetime = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = configuration[Constants.JwtKeyNames.Issuer],
                ValidAudience = configuration[Constants.JwtKeyNames.Audience],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
            };
        });
    }

    public static int GetUserId(this IPrincipal principal)
    {
        // Users from Auth.Api might store ID in 'sub' or a custom claim.
        // For now, we assume the existing claim structure or adapt it.
        // OpenIddict usually puts the User ID in 'sub'.
        
        var claim = ((ClaimsPrincipal)principal).FindFirst(ClaimTypes.NameIdentifier) 
                    ?? ((ClaimsPrincipal)principal).FindFirst("sub")
                    ?? ((ClaimsPrincipal)principal).FindFirst(Constants.Claims.UserData);

        if (claim == null) throw WbsktExceptions.UnableToGetClaim("UserId");
        return int.Parse(claim.Value);
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
        if (claim == null) return new HostString(string.Empty);

        var addrString = claim.Value.Split('|').Last();
        return new HostString(addrString);
    }

    public static Guid GetTokenId(this IEnumerable<Claim> claims)
    {
        var claim = claims.FirstOrDefault(c => c.Type == Constants.Claims.TokenId);
        return claim != null ? Guid.Parse(claim.Value) : Guid.Empty;
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
