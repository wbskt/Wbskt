using Hangfire;
using Hangfire.SqlServer;
using Serilog;
using Wbskt.Common;
using Wbskt.Workflow.Api.Actions;
using Wbskt.Workflow.Api.Core;
using Wbskt.Workflow.Api.Core.Abstractions;
using Wbskt.Workflow.Api.EventHandlers;
using Wbskt.Workflow.Api.HostedServices;
using Wbskt.Workflow.Api.Scheduling;
using Wbskt.Common.Enums;
using Wbskt.Common.Extensions;

namespace Wbskt.Workflow.Api;

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

        // Add services to the container.
        builder.Services.ConfigureCommonServices();

        builder.Services.AddDataProtection();
        builder.Services.AddHttpClient();

        builder.Services.AddSingleton<IWorkflowEngine, WorkflowEngine>();

        // Register Actions with Keys
        builder.Services.AddKeyedTransient<IAction, LogAction>(StepIdentifier.ActionLog);
        builder.Services.AddKeyedTransient<IAction, SendEmailAction>(StepIdentifier.ActionSendEmail);
        builder.Services.AddKeyedTransient<IAction, MakeHttpRequestAction>(StepIdentifier.ActionMakeHttpRequest);
        builder.Services.AddKeyedTransient<IAction, SendPayloadToClientAction>(StepIdentifier.ActionSendPayloadToClient);
        builder.Services.AddKeyedTransient<IAction, IfConditionAction>(StepIdentifier.ModifierIfCondition);

        builder.Services.AddHostedService<RealtimeTriggerService>();
        builder.Services.AddTransient<ClientDataReceivedEventHandler>();

        builder.Services.AddTransient<WorkflowSchedulerService>();

        // Add Hangfire services.
        builder.Services.AddHangfire(configuration => configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(builder.Configuration.GetConnectionString("HangfireConnection"), new SqlServerStorageOptions
            {
                CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                QueuePollInterval = TimeSpan.Zero,
                UseRecommendedIsolationLevel = true,
                DisableGlobalLocks = true
            }));

        // Add the Hangfire server.
        builder.Services.AddHangfireServer();

        builder.Services.AddWbsktAuthentication(builder.Configuration);

        builder.Services.AddAuthorization();

        builder.Services.AddControllers();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseHangfireDashboard();

        RecurringJob.AddOrUpdate<WorkflowSchedulerService>(
            "workflow-scheduler-job",
            service => service.TriggerDueWorkflows(),
            Cron.Minutely());

        app.MapControllers();

        app.Run();
    }
}
