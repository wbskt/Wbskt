using System.Security.Authentication;
using Microsoft.AspNetCore.Identity;
using Serilog;
using Wbskt.Common;
using Wbskt.Common.Extensions;
using Wbskt.Common.Records;
using Wbskt.Common.Services;
using Wbskt.Core.Service.Pipeline;
using Wbskt.Core.Service.Services;
using Wbskt.Core.Service.Services.Implementations;

namespace Wbskt.Core.Service;

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
        builder.Services.AddSingleton<IPasswordHasher<UserRecord>, PasswordHasher<UserRecord>>();

        builder.Services.AddSingleton<IAuthService, AuthService>();

        builder.Services.ConfigureCommonServices();

        // Register Background Services
        builder.Services.AddHostedService<ServerBackgroundService>();
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
