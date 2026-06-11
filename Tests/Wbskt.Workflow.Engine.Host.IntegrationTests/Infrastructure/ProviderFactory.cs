using Microsoft.Extensions.Configuration;
using Wbskt.Workflow.Providers;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

/// <summary>
/// Convenience factory: builds provider instances pointed at the integration-test database.
/// Each method <c>new</c>s the provider with an in-memory <see cref="IConfiguration"/>.
/// </summary>
internal static class ProviderFactory
{
    public static IConfiguration BuildConfiguration(string connectionString)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString
            })
            .Build();
    }

    public static WorkflowDefinitionProvider WorkflowDefinition(string connectionString)
        => new(BuildConfiguration(connectionString));

    public static RunProvider Run(string connectionString)
        => new(BuildConfiguration(connectionString));

    public static RunCountersProvider RunCounters(string connectionString)
        => new(BuildConfiguration(connectionString));

    public static BranchProvider Branch(string connectionString)
        => new(BuildConfiguration(connectionString));

    public static BookmarkProvider Bookmark(string connectionString)
        => new(BuildConfiguration(connectionString));

    public static HistoryEventProvider HistoryEvent(string connectionString)
        => new(BuildConfiguration(connectionString));

    public static SharedVariableProvider SharedVariable(string connectionString)
        => new(BuildConfiguration(connectionString));

    public static PendingTriggerEventProvider PendingTriggerEvent(string connectionString)
        => new(BuildConfiguration(connectionString));
}
