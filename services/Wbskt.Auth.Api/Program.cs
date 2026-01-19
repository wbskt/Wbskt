using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Serilog;
using OpenIddict.EntityFrameworkCore;
using Wbskt.Auth.Api.Data;
using Wbskt.Auth.Api.Models;
using Wbskt.Auth.Api.Middleware;
using Wbskt.Common;

namespace Wbskt.Auth.Api;

internal static class Program
{
    private static readonly string ProgramDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Constants.Application.AppFolderName);

    public static void Main(string[] args)
    {
        Environment.SetEnvironmentVariable(Constants.LoggingConstants.LogPath, ProgramDataPath);
        Environment.SetEnvironmentVariable(Constants.LoggingConstants.LogName, typeof(Program).Assembly.FullName);

        if (!Directory.Exists(ProgramDataPath))
        {
            Directory.CreateDirectory(ProgramDataPath);
        }
        
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = Directory.GetCurrentDirectory()
        });

        // Configure Serilog
        var serilogConfigPath = Path.Combine(builder.Environment.ContentRootPath, "..", "..", "Config", "serilog.json");
        builder.Configuration.AddJsonFile(serilogConfigPath, optional: false, reloadOnChange: true);
        Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).CreateLogger();
        builder.Host.UseSerilog(Log.Logger);

        // Configure Data Protection
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(ProgramDataPath, "Auth-Keys")))
            .SetApplicationName("Wbskt.Auth.Api");

        builder.Services.AddControllersWithViews();
        builder.Services.AddRazorPages();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        builder.Services.AddDbContext<AuthDbContext>(options =>
        {
            options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
            options.UseOpenIddict();
        });

        // Configure Identity
        builder.Services
            .AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireUppercase = true;
                options.Password.RequiredLength = 8;

                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddDefaultTokenProviders();

        // Configure OpenIddict
        builder.Services
            .AddOpenIddict()
            .AddCore(options =>
            {
                // Configure OpenIddict to use the Entity Framework Core stores and models.
                // Note: call ReplaceDefaultEntities() to use the default OpenIddict entities.
                options
                    .UseEntityFrameworkCore()
                    .UseDbContext<AuthDbContext>();
            })
            .AddServer(options =>
            {
                // Enable the authorization and token endpoints.
                options
                    .SetAuthorizationEndpointUris("connect/authorize")
                    .SetTokenEndpointUris("connect/token")
                    .SetLogoutEndpointUris("connect/logout");

                options.RegisterScopes(
                    Constants.Scopes.Email,
                    Constants.Scopes.Profile,
                    Constants.Scopes.OfflineAccess,
                    Constants.Scopes.OpenId,
                    Constants.Scopes.Roles,
                    Constants.Scopes.WbsktApi
                );

                // Enable flows.
                options.AllowAuthorizationCodeFlow();
                options.AllowClientCredentialsFlow();
                options.AllowPasswordFlow();
                options.AllowRefreshTokenFlow();

                // Register the signing and encryption credentials.
                if (builder.Environment.IsDevelopment())
                {
                    options
                        .AddDevelopmentEncryptionCertificate()
                        .AddDevelopmentSigningCertificate();
                }
                else
                {
                    // TODO: Configure production certificates
                    // options.AddEncryptionCertificate("thumbprint")
                    // options.AddSigningCertificate("thumbprint");
                }
                
                // Register the ASP.NET Core host and configure the ASP.NET Core-specific options.
                var aspNetCoreBuilder = options
                    .UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough();
                    
                if (builder.Environment.IsDevelopment()) 
                {
                    aspNetCoreBuilder.DisableTransportSecurityRequirement();
                }
            })
            // Register the OpenIddict validation components.
            .AddValidation(options =>
            {
                // Import the configuration from the local OpenIddict server instance.
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        builder.Services.AddHostedService<Worker>();

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("Default",
                policyBuilder =>
                {
                    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
                    if (allowedOrigins != null)
                    {
                        policyBuilder
                            .WithOrigins(allowedOrigins)
                            .AllowAnyMethod()
                            .AllowAnyHeader();
                    }
                }
            );
        });

        var app = builder.Build();

        app.UseSecurityHeaders(policyCollection =>
            policyCollection
                .AddDefaultSecurityHeaders()
                .AddContentSecurityPolicy(cspBuilder =>
                {
                    cspBuilder.AddDefaultSrc().Self().UnsafeInline();
                    cspBuilder.AddObjectSrc().None();
                    cspBuilder.AddFrameAncestors().None();
                })
                .AddPermissionsPolicy(policyBuilder =>
                {
                    policyBuilder.AddAccelerometer().None();
                    policyBuilder.AddAutoplay().None();
                    policyBuilder.AddCamera().None();
                    policyBuilder.AddEncryptedMedia().None();
                    policyBuilder.AddFullscreen().None();
                    policyBuilder.AddGeolocation().None();
                    policyBuilder.AddGyroscope().None();
                    policyBuilder.AddMagnetometer().None();
                    policyBuilder.AddMicrophone().None();
                    policyBuilder.AddMidi().None();
                    policyBuilder.AddPayment().None();
                    policyBuilder.AddPictureInPicture().None();
                    policyBuilder.AddSpeaker().None();
                    policyBuilder.AddSyncXHR().None();
                    policyBuilder.AddUsb().None();
                    policyBuilder.AddVR().None();
                }));

        app.UseMiddleware<ErrorHandlingMiddleware>();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseStaticFiles();

        app.UseCors("Default");

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapDefaultControllerRoute();

        app.Run();
    }
}