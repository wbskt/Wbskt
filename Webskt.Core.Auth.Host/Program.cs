using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Webskt.Core.Auth.Host.Middleware;
using Webskt.Core.Auth.Host.Providers;
using Webskt.Core.Auth.Host.Services;
using Webskt.Core.Constants;

namespace Webskt.Core.Auth.Host;

public static class Program
{
    private static readonly string ProgramDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Application.AppFolderName);

    public static void Main(string[] args)
    {
        Environment.SetEnvironmentVariable(LoggingConstants.LogPath, ProgramDataPath);
        Environment.SetEnvironmentVariable(LoggingConstants.LogName, typeof(Program).Namespace);

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
        var serilogInBinConfigPath = Path.Combine(builder.Environment.ContentRootPath, "serilog.json");
        var serilogConfigPath = Path.Combine(builder.Environment.ContentRootPath, "..", "Config", "serilog.json");

        // Load the shared configuration from the central Config folder
        builder.Configuration.AddJsonFile(serilogConfigPath, optional: true, reloadOnChange: true);

        // Load the local configuration from the bin folder, overriding any shared settings
        builder.Configuration.AddJsonFile(serilogInBinConfigPath, optional: true, reloadOnChange: true);

        // Apply environment-specific overrides (e.g., Development or Production specific settings)
        builder.Configuration.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);

        Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).CreateLogger();
        builder.Host.UseSerilog(Log.Logger);

        // Add services to the container.
        builder.Services.AddScoped<IAuthProvider, SqlAuthProvider>();
        builder.Services.AddScoped<IAuthService, AuthService>();

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

        builder.Services.AddControllers();

        builder.Services.AddOpenApi();

        var app = builder.Build();

        app.UseMiddleware<GlobalExceptionMiddleware>();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseHttpsRedirection();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();

        app.Run();
    }
}