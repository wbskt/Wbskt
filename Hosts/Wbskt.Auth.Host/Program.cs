using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using Serilog;
using Wbskt.Auth.Host.Extensions;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Configuration;
using Wbskt.Infrastructure.Mappers;
using Wbskt.Infrastructure.Middlewares;
using Wbskt.Infrastructure.Security;
using Wbskt.Primitives;
using Wbskt.Primitives.Constants;

namespace Wbskt.Auth.Host;

public static class Program
{
    private static readonly string ProgramDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Application.AppFolderName);

    public static async Task Main(string[] args)
    {
        Environment.SetEnvironmentVariable(Logging.LogPath, ProgramDataPath);
        Environment.SetEnvironmentVariable(Logging.LogName, typeof(Program).Namespace);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = Directory.GetCurrentDirectory()
        });
        
        builder.AddSharedConfiguration("serilog.json", "connectionstrings.json", "rabbitmq.json", "jwt.json");

        builder.Host.UseSerilog(builder.CreateSerilog());

        // Add services to the container.
        builder.Services.AddSingleton<AuthMetrics>();
        builder.Services.AddSingleton<IIdentityService, IdentityService>();
        builder.Services.AddScoped<IJwtService, JwtService>();
        builder.Services.AddScoped<IAuthProvider, SqlAuthProvider>();
        builder.Services.AddScoped<IAuthService, AuthService>();
        builder.Services.AddScoped<IWorkspaceProvider, WorkspaceProvider>();
        builder.Services.AddScoped<IWorkspaceService, WorkspaceService>();
        builder.Services.AddHttpContextAccessor();

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter(AuthMetrics.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddPrometheusExporter());

        // Register Keyed ReferenceMappers
        builder.Services.AddKeyedScoped<IReferenceMapper, ReferenceMapper<IWorkspaceProvider>>(ReferenceType.Workspace);

        // Event Bus
        builder.Services.AddRabbitMqEventBus(builder.Configuration);

        // Startup Tasks
        builder.Services.AddTransient<IStartupTask, FolderInitializationStartupTask>();

        var key = Encoding.ASCII.GetBytes(builder.Configuration["Jwt:Key"]!);
        builder.Services.AddAuthentication(x =>
        {
            x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(x =>
        {
            x.RequireHttpsMetadata = false;
            x.SaveToken = true;
            x.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = false,
                ValidateAudience = false
            };
        });

        builder.Services.AddAuthorization();

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
                if (allowedOrigins is { Length: > 0 })
                {
                    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
                }
                else if (builder.Environment.IsDevelopment())
                {
                    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
                }
            });
        });

        builder.Services.AddControllers();

        builder.Services.AddCustomOpenApi();

        var app = builder.Build();

        await app.RunStartupTasksAsync();

        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            KnownIPNetworks = { },
            KnownProxies = { }
        });

        app.UseMiddleware<GlobalExceptionMiddleware>();

        app.UseCors();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();

            app.MapCustomScalarApiReference();
        }

        app.UseAuthentication();
        app.UseMiddleware<IdentityMiddleware>();
        app.UseAuthorization();

        app.MapPrometheusScrapingEndpoint();
        app.MapGet("/healthz", () => Results.Ok()).AllowAnonymous();
        app.MapControllers();

        await app.RunAsync();
    }
}