using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.Workflow.Runtime.BranchResults;

/// <summary>Where a branch goes from a node's outbound port.</summary>
internal static class BranchRouting
{
    /// <summary>
    /// The node wired to <paramref name="outboundPort"/>, or the node the port names when it is a node id
    /// (a jump), or null when the port leads nowhere.
    /// </summary>
    public static Guid? ResolveNext(WorkflowDefinition definition, Guid currentNodeId, string outboundPort)
    {
        if (Guid.TryParse(outboundPort, out Guid jumpNodeId)
            && definition.Nodes.Any(candidate => candidate.NodeId == jumpNodeId))
        {
            return jumpNodeId;
        }

        Edge? edge = definition.Edges.SingleOrDefault(candidate => candidate.From.NodeId == currentNodeId && string.Equals(candidate.From.PortId, outboundPort, StringComparison.Ordinal));
        return edge?.To.NodeId;
    }

    /// <summary>As <see cref="ResolveNext"/>, for a fork, where a port that leads nowhere is an error.</summary>
    public static Guid ResolveForkTarget(WorkflowDefinition definition, Guid currentNodeId, string outboundPort)
    {
        Guid? resolvedNodeId = ResolveNext(definition, currentNodeId, outboundPort);
        if (resolvedNodeId is Guid nodeId)
        {
            return nodeId;
        }

        throw new InvalidOperationException($"Unable to resolve fork target port '{outboundPort}' from node {currentNodeId}.");
    }
}
