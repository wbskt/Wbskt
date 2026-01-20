using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Serilog;
using Wbskt.Common;
using Yarp.ReverseProxy.Transforms;

namespace Wbskt.Gateway.Api;

internal static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = Directory.GetCurrentDirectory()
        });

        var programDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Constants.Application.AppFolderName);
        Environment.SetEnvironmentVariable(Constants.LoggingConstants.LogPath, programDataPath);
        Environment.SetEnvironmentVariable(Constants.LoggingConstants.LogName, typeof(Program).Namespace);

        if (!Directory.Exists(programDataPath))
        {
            Directory.CreateDirectory(programDataPath);
        }

        // Configure Serilog
        var serilogConfigPath = Path.Combine(builder.Environment.ContentRootPath, "..", "..", "Config", "serilog.json");
        builder.Configuration.AddJsonFile(serilogConfigPath, optional: false, reloadOnChange: true);
        Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).CreateLogger();
        builder.Host.UseSerilog(Log.Logger);

        // Authentication Setup
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; 
        })
        .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
        {
            options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.Authority = builder.Configuration["Authentication:Authority"];
            options.ClientId = builder.Configuration["Authentication:ClientId"];
            options.ClientSecret = builder.Configuration["Authentication:ClientSecret"];
            options.ResponseType = builder.Configuration["Authentication:ResponseType"]!;
            options.CallbackPath = builder.Configuration["Authentication:CallbackPath"];
            options.SaveTokens = true;
            
            options.Scope.Clear();
            options.Scope.Add(Constants.Scopes.OpenId);
            options.Scope.Add(Constants.Scopes.Profile);
            options.Scope.Add(Constants.Scopes.WbsktApi);
            
            options.GetClaimsFromUserInfoEndpoint = true;
            options.RequireHttpsMetadata = false; // For Dev
            
            // Explicitly define configuration to bypass automatic discovery fetch issues
            // This is critical when running locally with HTTP to prevent "configuration missing" errors
            options.Configuration = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration
            {
                Issuer = options.Authority,
                AuthorizationEndpoint = $"{options.Authority}/connect/authorize",
                TokenEndpoint = $"{options.Authority}/connect/token",
                UserInfoEndpoint = $"{options.Authority}/connect/userinfo",
                EndSessionEndpoint = $"{options.Authority}/connect/logout",
                JwksUri = $"{options.Authority}/.well-known/jwks"
            };
        });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("Authenticated", policy => policy.RequireAuthenticatedUser());
        });

        // Add services to the container.
        var proxyConfig = builder.Configuration.GetSection("ReverseProxy");
        builder.Services.AddReverseProxy()
            .LoadFromConfig(proxyConfig)
            .AddTransforms(builderContext =>
            {
                builderContext.AddRequestTransform(async transformContext =>
                {
                    var accessToken = await transformContext.HttpContext.GetTokenAsync("access_token");
                    if (!string.IsNullOrEmpty(accessToken))
                    {
                        transformContext.ProxyRequest.Headers.Authorization = 
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                    }
                });
            });

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        // NOTE: HTTPS redirection and HSTS are disabled for local dev to avoid redirect loops on HTTP ports.
        // app.UseHttpsRedirection();
        // app.UseHsts();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/login", (string? returnUrl) =>
        {
            var redirectUri = !string.IsNullOrEmpty(returnUrl) ? returnUrl : "/";
            return Results.Challenge(new AuthenticationProperties { RedirectUri = redirectUri }, [OpenIdConnectDefaults.AuthenticationScheme]);
        });

        app.MapGet("/logout", () =>
        {
            return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
                [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
        });

        app.MapGet("/", (System.Security.Claims.ClaimsPrincipal user) => 
        {
            return $"WBSKT Gateway Active. User: {user.Identity?.Name ?? "Anonymous"}";
        });

        app.MapReverseProxy();

        app.Run();
    }
}
