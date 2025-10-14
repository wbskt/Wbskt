using Hangfire;
using Hangfire.SqlServer;
using Wbskt.Common;
using Wbskt.Workflow.Api.Actions;
using Wbskt.Workflow.Api.Core;
using Wbskt.Workflow.Api.Core.Abstractions;
using Wbskt.Workflow.Api.EventHandlers;
using Wbskt.Workflow.Api.HostedServices;
using Wbskt.Workflow.Api.Scheduling;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.ConfigureCommonServices();

builder.Services.AddSingleton<IWorkflowEngine, WorkflowEngine>();

builder.Services.AddTransient<LogAction>();
builder.Services.AddTransient<SendPayloadToClientAction>();
builder.Services.AddTransient<IfConditionModifier>();

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

builder.Services.AddControllers();

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseAuthorization();

app.UseHangfireDashboard();

RecurringJob.AddOrUpdate<WorkflowSchedulerService>(
    "workflow-scheduler-job",
    service => service.TriggerDueWorkflows(),
    Cron.Minutely());

app.MapControllers();

app.Run();
