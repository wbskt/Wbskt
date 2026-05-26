using System.Text;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Wbskt.EventBus.Abstractions;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Events;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Configuration;
using Wbskt.Infrastructure.Mappers;
using Wbskt.Infrastructure.Middlewares;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Extensions;
using Wbskt.Management.Host.Hubs;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Primitives;
using Wbskt.Primitives.Constants;
using Wbskt.Workflow.Abstraction.Validation;
namespace Wbskt.Management.Host;

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
        builder.Services.AddScoped<IJwtService, JwtService>();
        builder.Services.AddScoped<IRegistrationPolicyProvider, RegistrationPolicyProvider>();
        builder.Services.AddScoped<IRegistrationPolicyService, RegistrationPolicyService>();
        builder.Services.AddScoped<IClientProvider, ClientProvider>();
        builder.Services.AddScoped<IEventLogService, EventLogService>();
        builder.Services.AddScoped<IClientRegistrationService, ClientRegistrationService>();
        builder.Services.AddScoped<IClientService, ClientService>();
        builder.Services.AddScoped<IClientAuthService, ClientAuthService>();
        builder.Services.AddScoped<IWorkflowDefinitionService, WorkflowDefinitionService>();
        builder.Services.AddSingleton<WorkflowValidator>();
        
        builder.Services.AddTransient<AuthenticationForwardingHandler>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddHttpClient<IAuthServiceClient, AuthServiceClient>(client =>
        {
            client.BaseAddress = new Uri(builder.Configuration["Services:Auth"] 
                                         ?? throw new ArgumentNullException(nameof(client.BaseAddress), "Services:Auth configuration is missing."));
        })
        .AddHttpMessageHandler<AuthenticationForwardingHandler>();
        
        builder.Services.TryAddSingleton<IEventProvider, EventProvider>();

        // Event Bus & Logging
        var dbEventLoggingBusConfig = builder.Services.AddDatabaseEventLogging(builder.Configuration);

        builder.Services.AddRabbitMqEventBus(builder.Configuration, 
            configureBus: configurator =>
            {
                dbEventLoggingBusConfig(configurator);

                // Register Auto SignalR Forwarding Consumers
                configurator.AddAutoSignalRForwarding();
            });

        // Register Keyed ReferenceMappers
        builder.Services.AddKeyedScoped<IReferenceMapper, ReferenceMapper<IRegistrationPolicyProvider>>("RegistrationPolicy");
        builder.Services.AddKeyedScoped<IReferenceMapper, ReferenceMapper<IClientProvider>>("Client");

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
            x.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var isSignalR = context.HttpContext.Request.Path.StartsWithSegments("/hubs/notifications");
                    if (!isSignalR)
                    {
                        return Task.CompletedTask;
                    }

                    var accessToken = context.Request.Query["access_token"];
                    if (string.IsNullOrWhiteSpace(accessToken))
                    {
                        return Task.CompletedTask;
                    }

                    context.Token = accessToken;

                    return Task.CompletedTask;

                }
            };
        });

        builder.Services.AddAuthorization();

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAll",
                policyBuilder =>
                {
                    policyBuilder
                        .SetIsOriginAllowed(_ => true)
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials();
                }
            );
        });

        builder.Services.AddControllers();
        builder.Services.AddSignalR().AddJsonProtocol(options =>
        {
            options.PayloadSerializerOptions.TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { IgnoreSignalRPrivateProperties }
            };
        });
        builder.Services.AddCustomOpenApi();

        var app = builder.Build();

        await app.RunStartupTasksAsync();

        app.UseMiddleware<GlobalExceptionMiddleware>();

        app.UseCors("AllowAll");

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();

            app.MapCustomScalarApiReference();
        }

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapHub<NotificationHub>("/hubs/notifications");

        await app.RunAsync();
    }

    private static void IgnoreSignalRPrivateProperties(JsonTypeInfo typeInfo)
    {
        // 1. Quick Exit: Only process objects that implement IEvent
        if (typeInfo.Kind != JsonTypeInfoKind.Object || !typeof(IEvent).IsAssignableFrom(typeInfo.Type))
        {
            return;
        }
        
        // 2. Scan properties
        foreach (var propertyInfo in typeInfo.Properties)
        {
            // Check property itself
            var hasPrivateAttribute = propertyInfo.AttributeProvider?.GetCustomAttributes(typeof(SignalRPrivateAttribute), false).Length > 0;
            
            // Check interfaces (only if not already found)
            if (!hasPrivateAttribute)
            {
                hasPrivateAttribute = typeInfo.Type.GetInterfaces()
                    .Select(i => i.GetProperty(propertyInfo.Name))
                    .Any(p => p != null && p.GetCustomAttributes(typeof(SignalRPrivateAttribute), false).Length > 0);
            }
            
            if (hasPrivateAttribute)
            {
                propertyInfo.ShouldSerialize = (_, _) => false;
            }
        }
    }
}