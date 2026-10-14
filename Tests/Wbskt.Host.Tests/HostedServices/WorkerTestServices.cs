using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;

namespace Wbskt.Host.Tests.HostedServices;

/// <summary>What a periodic worker needs to be built in a test: real scopes over test doubles, and options.</summary>
internal static class WorkerTestServices
{
    /// <summary>
    /// A real container holding each double under every interface it implements, so a worker resolves
    /// its dependencies from a scope exactly as it does in the engine.
    /// </summary>
    public static IServiceScopeFactory Scopes(params object[] doubles)
    {
        var services = new ServiceCollection();
        foreach (object instance in doubles)
        {
            foreach (Type contract in instance.GetType().GetInterfaces())
            {
                services.AddSingleton(contract, instance);
            }
        }

        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    public static IOptions<WorkflowEngineOptions> Options(WorkflowEngineOptions? options = null) =>
        Microsoft.Extensions.Options.Options.Create(options ?? new WorkflowEngineOptions());
}
