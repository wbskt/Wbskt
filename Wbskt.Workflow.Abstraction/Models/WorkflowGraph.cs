using Wbskt.Workflow.Abstraction.Models.Nodes;

namespace Wbskt.Workflow.Abstraction.Models;

/// <summary>
/// Graph walks over a <see cref="WorkflowDefinition"/>'s edges, shared by the runtime and the
/// validator so both agree on what "downstream" means.
/// </summary>
public static class WorkflowGraph
{
    /// <summary>
    /// Breadth-first search from a node's outbound port for the nearest node of type
    /// <typeparamref name="TNode"/>. Returns null when none is reachable.
    ///
    /// Cycle-safe: workflow graphs legitimately contain back-edges (a loop body wiring back to its
    /// ForEach), so visited nodes are tracked and never re-expanded.
    /// </summary>
    public static TNode? FindDownstream<TNode>(WorkflowDefinition definition, Guid fromNodeId, string fromPortId)
        where TNode : BaseNode
    {
        ArgumentNullException.ThrowIfNull(definition);

        Dictionary<Guid, BaseNode> nodesById = definition.Nodes
            .GroupBy(node => node.NodeId)
            .ToDictionary(group => group.Key, group => group.First());

        var visited = new HashSet<Guid>();
        var queue = new Queue<Guid>();

        // Seed with everything reachable from the specified port only - the caller is asking about
        // one branch of the graph, not the node's whole neighbourhood.
        foreach (Edge edge in definition.Edges.Where(e => e.From.NodeId == fromNodeId && e.From.PortId == fromPortId))
        {
            queue.Enqueue(edge.To.NodeId);
        }

        while (queue.Count > 0)
        {
            Guid currentId = queue.Dequeue();
            if (!visited.Add(currentId) || !nodesById.TryGetValue(currentId, out BaseNode? current))
            {
                continue;
            }

            if (current is TNode match)
            {
                return match;
            }

            foreach (Edge edge in definition.Edges.Where(e => e.From.NodeId == currentId))
            {
                if (!visited.Contains(edge.To.NodeId))
                {
                    queue.Enqueue(edge.To.NodeId);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The node an edge from <paramref name="fromNodeId"/>'s <paramref name="fromPortId"/> leads to,
    /// or null when the port has no outbound edge.
    /// </summary>
    public static Guid? ResolveTarget(WorkflowDefinition definition, Guid fromNodeId, string fromPortId)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return definition.Edges
            .Where(edge => edge.From.NodeId == fromNodeId && edge.From.PortId == fromPortId)
            .Select(edge => (Guid?)edge.To.NodeId)
            .FirstOrDefault();
    }
}
