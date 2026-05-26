using Serilog;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Configuration;
using Wbskt.Infrastructure.Middlewares;
using Wbskt.Primitives;
using Wbskt.Primitives.Constants;
using Wbskt.Workflow.Engine.Host.Extensions;
using Wbskt.Workflow.Engine.Host.HostedServices;
using Wbskt.Workflow.Extensions;

namespace Wbskt.Workflow.Engine.Host;

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

        builder.AddSharedConfiguration("serilog.json", "connectionstrings.json", "rabbitmq.json");

        builder.Host.UseSerilog(builder.CreateSerilog());

        builder.Services.AddRabbitMqEventBus(builder.Configuration);
        builder.Services.AddHttpClient();
        builder.Services.AddTransient<IStartupTask, FolderInitializationStartupTask>();
        builder.Services.AddWorkflowProviders();
        builder.Services.AddWorkflowRuntime();
        builder.Services.AddHostedService<BranchExecutionPump>();
        builder.Services.AddHostedService<BookmarkScheduler>();
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

        var app = builder.Build();

        await app.RunStartupTasksAsync();

        app.UseMiddleware<GlobalExceptionMiddleware>();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        await app.RunAsync();
    }
}
