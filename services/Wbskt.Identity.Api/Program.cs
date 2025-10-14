using System.Security.Authentication;
using Microsoft.AspNetCore.Identity;
using Serilog;
using Wbskt.Common;
using Wbskt.Common.Events;
using Wbskt.Common.Extensions;
using Wbskt.Common.Readers;
using Wbskt.Common.Readers.Database;
using Wbskt.Common.Records;
using Wbskt.Common.Services;
using Wbskt.Common.Writers;
using Wbskt.EventBus;
using Wbskt.Identity.Api.EventHandlers;
using Wbskt.Identity.Api.Pipeline;
using Wbskt.Identity.Api.Services;
using Wbskt.Identity.Api.Services.Implementations;

namespace Wbskt.Identity.Api;

public static class Program
{
    private static readonly string ProgramDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Constants.Application.AppFolderName);

    public static async Task Main(string[] args)
    {
        Environment.SetEnvironmentVariable(Constants.LoggingConstants.LogPath, ProgramDataPath);
        Environment.SetEnvironmentVariable(nameof(Constants.ServerType), nameof(Constants.ServerType.CoreServer));

        if (!Directory.Exists(ProgramDataPath))
        {
            Directory.CreateDirectory(ProgramDataPath);
        }

        var builder = WebApplication.CreateBuilder(args);

        // Configure Serilog
        Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).CreateLogger();

        builder.Host.UseSerilog(Log.Logger);
        builder.WebHost.UseKestrel().ConfigureKestrel((_, options) => { options.ConfigureHttpsDefaults(httpsOptions => { httpsOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13; }); });

        // Add Windows Service hosting
        builder.Host.UseWindowsService();

        // Add services to the container.
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, CurrentUser>();

        builder.Services.AddSingleton<IPasswordHasher<UserRecord>, PasswordHasher<UserRecord>>();

        builder.Services.AddSingleton<IAuthService, AuthService>();
        builder.Services.AddSingleton<IPolicyService, PolicyService>();
        builder.Services.AddSingleton<IRegistrationService, RegistrationService>();
        
        builder.Services.ConfigureCommonServices();
        builder.Services.AddTransient<PolicyCacheHandler>();
        builder.Services.AddTransient<WorkflowCacheHandler>();

        builder.Services.AddAuthentication(opt =>
            {
                opt.DefaultAuthenticateScheme = Constants.AuthSchemes.UserScheme;
                opt.DefaultChallengeScheme = Constants.AuthSchemes.UserScheme;
            })
            .AddUserAuthScheme(builder.Configuration)
            .AddClientAuthScheme(builder.Configuration)
            .AddSocketServerAuthScheme(builder.Configuration);

        builder.Services.AddAuthorization();

        builder.Services.AddControllers();

        var app = builder.Build();

        var eventBus = app.Services.GetRequiredService<IEventBus>();
        eventBus.Subscribe<PolicyCreatedEvent, PolicyCacheHandler>();
        eventBus.Subscribe<PolicyUpdatedEvent, PolicyCacheHandler>();
        eventBus.Subscribe<PolicyDeletedEvent, PolicyCacheHandler>();

        eventBus.Subscribe<WorkflowCreatedEvent, WorkflowCacheHandler>();
        eventBus.Subscribe<WorkflowUpdatedEvent, WorkflowCacheHandler>();
        eventBus.Subscribe<WorkflowDeletedEvent, WorkflowCacheHandler>();

        eventBus.Subscribe<WorkflowStepsChangedEvent, WorkflowStepsCacheHandler>();

        // Configure the HTTP request pipeline.
        app.UseMiddleware<ExceptionMiddleware>();
        app.UseHttpsRedirection();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseWebSockets();

        app.MapControllers();

        var cancellationService = app.Services.GetRequiredService<ICancellationService>();
        app.Lifetime.ApplicationStarted.Register(() => { OnStarted(app.Services); });

        app.Lifetime.ApplicationStopping.Register(() => { OnStopping(app.Services); });

        await app.RunAsync(cancellationService.GetToken());
    }

    private static void OnStopping(IServiceProvider serviceProvider)
    {
        var cancellationService = serviceProvider.GetRequiredService<ICancellationService>();
        cancellationService.Cancel().Wait();
    }

    private  static void OnStarted(IServiceProvider serviceProvider)
    {
        // TODO: if anything needs to be done when service is starting
    }
}
