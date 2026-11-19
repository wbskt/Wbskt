using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;

namespace Wbskt.Workflow.Runtime;

/// <summary>
/// The quorum a cohort's aggregator is stamped with. A Quorum asking for more branches than the
/// cohort has waits for all of them, and one with no count (saved before validation required one)
/// waits for one, as it always did. Without the cap the aggregator saw the quorum as already out of
/// reach and released the first arrival.
/// </summary>
public static class JoinQuorum
{
    public static int For(JoinConfig? config, int expectedCount)
    {
        if (config is not { Mode: JoinMode.Quorum })
        {
            return config?.QuorumCount ?? 0;
        }

        return Math.Min(Math.Max(config.QuorumCount ?? 0, 1), expectedCount);
    }
}
