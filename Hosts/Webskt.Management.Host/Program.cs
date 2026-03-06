using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using Webskt.Common.Data;
using Webskt.Common.Infrastructure;
using Webskt.Common.Logging;
using Webskt.Common.Mappers;
using Webskt.Common.Middlewares;
using Webskt.Common.Security;
using Webskt.EventBus.RabbitMQ;
using Webskt.Foundation.Abstraction;
using Webskt.Foundation.Abstraction.Constants;
using Webskt.Management.Host.Extensions;
using Webskt.Management.Host.Hubs;
using Webskt.Management.Host.Providers;
using Webskt.Management.Host.Services;
using Webskt.Management.Host.Services.Clients;
using Webskt.Workflow.Providers;

namespace Webskt.Management.Host;

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

        builder.Host.UseSerilog(builder.CreateSerilog());

        // Add services to the container.
        builder.Services.AddScoped<IJwtService, JwtService>();
        builder.Services.AddScoped<IRegistrationPolicyProvider, RegistrationPolicyProvider>();
        builder.Services.AddScoped<IRegistrationPolicyService, RegistrationPolicyService>();
        builder.Services.AddScoped<IClientProvider, ClientProvider>();
        builder.Services.AddScoped<IWorkflowProvider, WorkflowProvider>();
        builder.Services.AddScoped<IWorkflowService, WorkflowService>();
        builder.Services.AddScoped<IClientRegistrationService, ClientRegistrationService>();
        builder.Services.AddScoped<IClientService, ClientService>();
        builder.Services.AddScoped<IClientAuthService, ClientAuthService>();
        
        builder.Services.AddTransient<AuthenticationForwardingHandler>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddHttpClient<IAuthServiceClient, AuthServiceClient>(client =>
        {
            client.BaseAddress = new Uri(builder.Configuration["Services:Auth"] 
                                         ?? throw new ArgumentNullException("Services:Auth configuration is missing."));
        })
        .AddHttpMessageHandler<AuthenticationForwardingHandler>();
        
        builder.Services.AddWebsktEventDataServices();

        // Event Bus & Logging
        var busConfig = builder.Services.AddDatabaseEventLogging();
        builder.Services.AddRabbitMQEventBus(builder.Configuration, configureBus: busConfig);

        // Register Keyed ReferenceMappers
        builder.Services.AddKeyedScoped<IReferenceMapper, ReferenceMapper<IRegistrationPolicyProvider>>("RegistrationPolicy");
        builder.Services.AddKeyedScoped<IReferenceMapper, ReferenceMapper<IClientProvider>>("Client");
        builder.Services.AddKeyedScoped<IReferenceMapper, ReferenceMapper<IWorkflowProvider>>("Workflow");

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
                policy.AllowAnyOrigin()
                      .AllowAnyHeader()
                      .AllowAnyMethod();
            });
        });

        builder.Services.AddControllers();
        builder.Services.AddSignalR();
        builder.Services.AddOpenApi();

        var app = builder.Build();

        await app.RunStartupTasksAsync();

        app.UseMiddleware<GlobalExceptionMiddleware>();

        app.UseCors();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();

            app.MapScalarApiReference();
        }

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapHub<NotificationHub>("/hubs/notifications");

        await app.RunAsync();
    }
}