using System.Security.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Serilog;
using Wbskt.Common;
using Wbskt.Common.Events;
using Wbskt.Common.Extensions;
using Wbskt.Common.Records;
using Wbskt.Common.Services;
using Wbskt.EventBus;
using Wbskt.Management.Api.EventHandlers;
using Wbskt.Management.Api.Pipeline;
using Wbskt.Management.Api.Services;
using Wbskt.Management.Api.Services.Implementations;

namespace Wbskt.Management.Api;

internal static class Program
{
    private static readonly string ProgramDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Constants.Application.AppFolderName);

    public static async Task Main(string[] args)
    {
        Environment.SetEnvironmentVariable(Constants.LoggingConstants.LogPath, ProgramDataPath);
        Environment.SetEnvironmentVariable(Constants.LoggingConstants.LogName, typeof(Program).Assembly.FullName);
        Environment.SetEnvironmentVariable(nameof(Constants.ServerType), nameof(Constants.ServerType.CoreServer));

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
        builder.Configuration.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);

        Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).CreateLogger();
        builder.Host.UseSerilog(Log.Logger);

        // Add services to the container.
        builder.Services.AddDataProtection().DisableAutomaticKeyGeneration();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<ICurrentUser, CurrentUser>();

        builder.Services.AddSingleton<IPolicyService, PolicyService>();
        builder.Services.AddSingleton<IRegistrationService, RegistrationService>();
        
        builder.Services.ConfigureCommonServices();
        builder.Services.AddSingleton<PolicyCacheHandler>();
        builder.Services.AddSingleton<WorkflowCacheHandler>();
        builder.Services.AddSingleton<WorkflowStepsCacheHandler>();

        builder.Services.AddWbsktAuthentication(builder.Configuration);

        builder.Services.AddAuthorization();

        builder.Services.AddControllers().AddJsonOptions(jsonOptions =>
        {
            jsonOptions.JsonSerializerOptions.PropertyNamingPolicy = null;
        });

        var app = builder.Build();

        var eventBus = app.Services.GetRequiredService<IEventBus>();
        eventBus.Subscribe<PolicyCreatedEvent, PolicyCacheHandler>();
        eventBus.Subscribe<PolicyUpdatedEvent, PolicyCacheHandler>();
        eventBus.Subscribe<PolicyDeletedEvent, PolicyCacheHandler>();

        eventBus.Subscribe<WorkflowCreatedEvent, WorkflowCacheHandler>();
        eventBus.Subscribe<WorkflowUpdatedEvent, WorkflowCacheHandler>();
        eventBus.Subscribe<WorkflowDeletedEvent, WorkflowCacheHandler>();

        eventBus.Subscribe<WorkflowStepsChangedEvent, WorkflowStepsCacheHandler>();

        app.UseMiddleware<ExceptionMiddleware>();

        app.UseAuthentication();
        app.UseAuthorization();

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
