using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Wbskt.Primitives;

namespace Wbskt.Infrastructure.Hosting;

public static class StartupTasksExtensions
{
    /// <summary>Runs every registered <see cref="IStartupTask"/>, in registration order, before the host serves.</summary>
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
