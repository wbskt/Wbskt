using Webskt.Common.Abstraction.Interfaces;

namespace Webskt.Management.Host.Extensions;

public static class StartupTasksExtensions
{
    public static async Task RunStartupTasksAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var tasks = scope.ServiceProvider.GetServices<IStartupTask>();

        foreach (var task in tasks)
        {
            await task.ExecuteAsync(app.Lifetime.ApplicationStopping);
        }
    }
}
