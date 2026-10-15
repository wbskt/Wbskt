using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;

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

    /// <summary>
    /// The Join the cohort opened by <paramref name="cohortNodeId"/> (a Fork's branch ports, or a
    /// ParallelForEach's "body") converges on, or null when its branches reach none at its own level.
    ///
    /// Not simply the nearest Join downstream: for a Fork nested inside a branch, the nearest one is the
    /// inner Join, and stamping its mode and node id onto the outer cohort's aggregator made the outer
    /// cohort converge by the inner Join's rules and spawn a failed branch's continuation in the wrong
    /// place. See <see cref="WalkCohort"/>.
    /// </summary>
    public static JoinNode? FindJoinForCohort(WorkflowDefinition definition, Guid cohortNodeId, IEnumerable<string> ports)
    {
        return WalkCohort(definition, cohortNodeId, ports).Joins.FirstOrDefault();
    }

    /// <summary>
    /// Walks every path out of <paramref name="ports"/> of <paramref name="cohortNodeId"/>, tracking
    /// cohort nesting: a Fork or ParallelForEach on the way opens an inner cohort, whose paths are
    /// followed to its own Join and on from there at the outer level again. Returns the Joins reached at
    /// the cohort's own level (nearest first) and whether some path ends at that level without reaching
    /// one - at an End node or through a port with no edge. A FailRun node is not such an end: it fails
    /// the whole run, so the join never needs to converge.
    ///
    /// <paramref name="outputPorts"/> names the ports to follow out of an ordinary node, so a caller that
    /// knows a node's expected ports can see one with no edge. Without it, every outbound edge is
    /// followed and only the ports of the walk's own control nodes are checked for a missing edge.
    /// </summary>
    public static CohortWalk WalkCohort(
        WorkflowDefinition definition,
        Guid cohortNodeId,
        IEnumerable<string> ports,
        Func<BaseNode, IEnumerable<string>>? outputPorts = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        Dictionary<Guid, BaseNode> nodesById = definition.Nodes
            .GroupBy(node => node.NodeId)
            .ToDictionary(group => group.Key, group => group.First());
        ILookup<(Guid NodeId, string PortId), Guid> targetsByPort = definition.Edges
            .ToLookup(edge => (edge.From.NodeId, edge.From.PortId), edge => edge.To.NodeId);

        var joins = new List<JoinNode>();
        bool endsBeforeJoin = false;
        // A node is visited once per nesting depth; the depth is bounded so a Fork wired back into its
        // own branch cannot nest forever. Breadth-first, so the nearest Join comes first.
        var visited = new HashSet<(Guid NodeId, int Depth)>();
        var pending = new Queue<(Guid NodeId, int Depth)>();

        void Follow(Guid fromNodeId, string portId, int depth)
        {
            bool any = false;
            foreach (Guid target in targetsByPort[(fromNodeId, portId)])
            {
                any = true;
                pending.Enqueue((target, depth));
            }

            if (!any && depth == 0)
            {
                endsBeforeJoin = true;
            }
        }

        foreach (string port in ports)
        {
            Follow(cohortNodeId, port, 0);
        }

        while (pending.Count > 0)
        {
            (Guid nodeId, int depth) = pending.Dequeue();
            if (depth > nodesById.Count || !visited.Add((nodeId, depth)) || !nodesById.TryGetValue(nodeId, out BaseNode? node))
            {
                continue;
            }

            switch (node)
            {
                case JoinNode join when depth == 0:
                    if (!joins.Contains(join))
                    {
                        joins.Add(join);
                    }
                    break;
                case JoinNode:
                    Follow(nodeId, "default", depth - 1);
                    break;
                case ForkNode { Config: not null } fork:
                    foreach (string branch in fork.Config.Branches)
                    {
                        Follow(nodeId, branch, depth + 1);
                    }
                    break;
                case ParallelForEachNode:
                    Follow(nodeId, "body", depth + 1);
                    // No items: the branch carries on past the node without opening a cohort.
                    Follow(nodeId, "empty", depth);
                    break;
                case EndNode:
                    endsBeforeJoin |= depth == 0;
                    break;
                case FailRunNode:
                    break;
                default:
                    IEnumerable<string> nodePorts = outputPorts?.Invoke(node)
                        ?? definition.Edges.Where(edge => edge.From.NodeId == nodeId).Select(edge => edge.From.PortId).Distinct();
                    foreach (string port in nodePorts)
                    {
                        Follow(nodeId, port, depth);
                    }
                    break;
            }
        }

        return new CohortWalk(joins, endsBeforeJoin);
    }
}

/// <summary>What <see cref="WorkflowGraph.WalkCohort"/> found: the Joins reached at the cohort's own level, nearest first, and whether some path ends at that level before reaching one.</summary>
public sealed record CohortWalk(IReadOnlyList<JoinNode> Joins, bool EndsBeforeJoin);
