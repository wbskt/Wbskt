using System.Text.Json;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Integrations;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Actions;

/// <summary>
/// A step that sends through an integration: fill the config's templates, resolve the integration the
/// step names, send, record the integration as used and hand on the reply's <c>status</c>. A kind
/// supplies its integration type, its send call and, where the service returns one, the key its
/// reply's ids are written under.
/// </summary>
/// <remarks>
/// A channel decides whether a failure is worth retrying: a request that is wrong (an unknown chat, a
/// revoked token, a refused recipient) fails the same way every time, while a service that is down or
/// rate-limiting is what the retry policy is for.
/// </remarks>
internal abstract class IntegrationStepExecutor<TNode, TConfig>(IIntegrationResolver resolver, ActionTemplates templates) : INodeExecutor
    where TNode : BaseNode
    where TConfig : class
{
    public abstract string Kind { get; }

    protected abstract IntegrationType IntegrationType { get; }

    /// <summary>Where the sent message's ids go in the step's output, or null when the service returns none.</summary>
    protected virtual string? MetadataKey => null;

    protected abstract TConfig? ConfigOf(TNode node);

    protected abstract Guid? IntegrationRefOf(TConfig config);

    protected abstract Task<ChannelResult> SendAsync(ResolvedIntegration integration, TConfig config, CancellationToken ct);

    /// <summary>A check on the filled config that fails the step before anything is looked up.</summary>
    protected virtual NodeExecutionResult.Fail? Check(TConfig config) => null;

    /// <summary>Outputs a kind adds beside <c>status</c>.</summary>
    protected virtual void AddOutputs(TConfig config, Dictionary<string, JsonElement> output)
    {
    }

    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var node = (TNode)ctx.Node;
        TConfig config = await templates.FillAsync(
            ctx,
            ConfigOf(node) ?? throw new InvalidOperationException($"{Kind} node {node.NodeId} is missing config."),
            ct);

        if (Check(config) is { } invalid)
        {
            return invalid;
        }

        var resolution = await resolver.ResolveAsync(ctx.Branch, IntegrationRefOf(config), IntegrationType, ct);
        if (resolution.Failure is not null)
        {
            return resolution.Failure;
        }

        var integration = resolution.Integration!;
        var result = await SendAsync(integration, config, ct);
        if (!result.Ok)
        {
            return new NodeExecutionResult.Fail(result.Code!, result.Message!, result.Retryable, null);
        }

        await resolver.MarkUsedAsync(integration, ct);
        var output = new Dictionary<string, JsonElement>
        {
            ["status"] = JsonSerializer.SerializeToElement(result.Status ?? 200)
        };
        if (MetadataKey is { } key && result.Metadata is { } sent)
        {
            output[key] = sent;
        }

        AddOutputs(config, output);
        return new NodeExecutionResult.Continue("default", output);
    }
}
