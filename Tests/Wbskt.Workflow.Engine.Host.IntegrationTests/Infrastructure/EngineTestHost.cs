using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Extensions;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

/// <summary>
/// Builds the engine's composition root as a test can use it.
///
/// <c>AddWorkflowEngine</c> alone is not enough to construct every node executor. It registers all
/// 21 of them, but three of their dependencies are deliberately not its to provide:
/// <see cref="IDeviceCommandPublisher"/> and <see cref="IToastPublisher"/> are outbound adapters onto
/// RabbitMQ, and <c>IHttpClientFactory</c> comes from <c>AddHttpClient</c>. The engine host supplies
/// all three in <c>Program.cs</c> so that <c>Wbskt.Workflow</c> takes no dependency on a transport.
///
/// The practical consequence is that the gap only appears when something resolves the executor
/// registry — which is why two tests here failed on it one dependency at a time. Assembling the
/// container in one place means the next test that needs a real run gets a working one, and a newly
/// added executor dependency fails here rather than in whichever test happens to resolve first.
/// </summary>
internal static class EngineTestHost
{
    /// <summary>
    /// Services wired to the integration database, with the host-side adapters stubbed. Returned
    /// un-built so a caller can still override — a recording dispatcher, a real publisher, a fake
    /// clock — before calling <c>BuildServiceProvider</c>.
    /// </summary>
    public static ServiceCollection BuildServices(string connectionString)
    {
        var services = new ServiceCollection();
        IConfiguration configuration = ProviderFactory.BuildConfiguration(connectionString);

        services.AddSingleton(configuration);
        services.AddWorkflowEngine(configuration);
        services.AddLogging();

        // Named clients are not configured here: CreateClient(name) falls back to a default-configured
        // client for an unregistered name, and the host's handler tuning (no auto-redirect on
        // workflow-webhook, the telegram timeout) is host policy rather than anything these tests assert.
        services.AddHttpClient();

        services.AddScoped<IDeviceCommandPublisher, NoopDeviceCommandPublisher>();
        services.AddScoped<IToastPublisher, NoopToastPublisher>();

        return services;
    }
}

/// <summary>
/// No-op stand-in for the engine host's event-bus-backed device command publisher. A no-op rather
/// than a recording double because the tests using it are about a run reaching a terminal state, not
/// about what was published; anything asserting on published commands should register its own.
/// </summary>
internal sealed class NoopDeviceCommandPublisher : IDeviceCommandPublisher
{
    public Task PublishCommandAsync(
        Guid clientRefId,
        int workspaceId,
        string command,
        string payload,
        CancellationToken ct) => Task.CompletedTask;
}

/// <summary>No-op stand-in for the engine host's toast publisher, for the same reason.</summary>
internal sealed class NoopToastPublisher : IToastPublisher
{
    public Task PublishToastAsync(
        Guid workflowRefId,
        int workflowDefinitionId,
        Guid runRefId,
        int workspaceId,
        string title,
        string message,
        CancellationToken ct) => Task.CompletedTask;
}
