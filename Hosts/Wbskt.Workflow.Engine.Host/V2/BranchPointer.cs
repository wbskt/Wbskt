namespace Wbskt.Workflow.Engine.Host.V2;

/// <summary>
/// A unit of work placed onto the branch execution channel.
/// It combines the node to execute next with the branch state to use.
/// </summary>
public sealed class BranchPointer
{
    public required Guid NodeId { get; init; }
    public required BranchContext Branch { get; init; }
}

