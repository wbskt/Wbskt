using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Wbskt.Host.Tests.Runtime;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Host.Tests.Architecture;

/// <summary>
/// Pins <see cref="NodeKinds.All"/> as the single description of the node kinds: every model has a
/// descriptor, every descriptor describes its model truthfully, and the executors registered at
/// runtime line up with it one to one.
/// </summary>
public sealed class NodeKindDescriptorTests
{
    /// <summary>
    /// A descriptor with no executor publishes cleanly and then fails the run mid-way with
    /// NODE_KIND_NOT_SUPPORTED; an executor with no descriptor can never be reached, because the
    /// converter cannot build its node and the validator rejects its kind. Executors are registered by
    /// hand, so this is what keeps the two lists together.
    /// </summary>
    [Fact]
    public void Every_descriptor_has_exactly_one_executor_and_every_executor_has_a_descriptor()
    {
        using ServiceProvider provider = NodeExecutorRegistryTests.BuildRuntime();
        using IServiceScope scope = provider.CreateScope();

        Dictionary<string, int> executorsByKind = scope.ServiceProvider
            .GetServices<INodeExecutor>()
            .GroupBy(executor => executor.Kind, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        string[] notExactlyOne = NodeKinds.All
            .Where(descriptor => executorsByKind.GetValueOrDefault(descriptor.Kind) != 1)
            .Select(descriptor => $"{descriptor.Kind} ({executorsByKind.GetValueOrDefault(descriptor.Kind)})")
            .ToArray();
        Assert.True(notExactlyOne.Length == 0, $"Descriptor(s) without exactly one executor: {string.Join(", ", notExactlyOne)}");

        string[] undescribed = executorsByKind.Keys.Where(kind => !NodeKinds.TryGet(kind, out _)).ToArray();
        Assert.True(undescribed.Length == 0, $"Executor registered for kind(s) with no descriptor: {string.Join(", ", undescribed)}");
    }

    [Fact]
    public void Kinds_are_unique_and_NodeKind_All_is_the_descriptor_list()
    {
        Assert.Equal(NodeKinds.All.Count, NodeKinds.All.Select(descriptor => descriptor.Kind).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(NodeKinds.All.Select(descriptor => descriptor.Kind).Order(StringComparer.Ordinal), NodeKind.All.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_concrete_node_model_has_a_descriptor()
    {
        // A model with no descriptor cannot be read back from JSON: the converter looks its type up by kind.
        Type[] models = typeof(BaseNode).Assembly.GetTypes()
            .Where(type => typeof(BaseNode).IsAssignableFrom(type) && !type.IsAbstract)
            .ToArray();

        string[] missing = models
            .Where(model => NodeKinds.All.All(descriptor => descriptor.ModelType != model))
            .Select(model => model.Name)
            .ToArray();
        Assert.True(missing.Length == 0, $"Node model(s) with no descriptor: {string.Join(", ", missing)}");
        Assert.Equal(models.Length, NodeKinds.All.Count);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_descriptor_describes_its_model(string kind)
    {
        Assert.True(NodeKinds.TryGet(kind, out NodeKindDescriptor? descriptor));

        // The model reports the same kind it is registered under; Kind is a constant on every model,
        // so an uninitialised instance answers it.
        Assert.True(typeof(BaseNode).IsAssignableFrom(descriptor.ModelType) && descriptor.ModelType.IsSealed, $"{descriptor.ModelType.Name} is not a sealed BaseNode");
        var model = (BaseNode)RuntimeHelpers.GetUninitializedObject(descriptor.ModelType);
        Assert.Equal(kind, model.Kind);

        // The config type is the model's own Config property, or null for a kind with none (End).
        PropertyInfo? config = descriptor.ModelType.GetProperty("Config", BindingFlags.Public | BindingFlags.Instance);
        Assert.Equal(config?.PropertyType, descriptor.ConfigType);

        // The category is the one the kind's prefix spells.
        string prefix = kind[..kind.IndexOf(':')];
        Assert.Equal(prefix, descriptor.Category.ToString().ToLowerInvariant());
    }

    public static TheoryData<string> Kinds()
    {
        var data = new TheoryData<string>();
        foreach (NodeKindDescriptor descriptor in NodeKinds.All)
        {
            data.Add(descriptor.Kind);
        }

        return data;
    }
}
