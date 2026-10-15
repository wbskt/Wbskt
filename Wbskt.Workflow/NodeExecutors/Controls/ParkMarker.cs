using System.Text.Json;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

/// <summary>
/// The local-state marker an await-style node (AwaitSignal, WaitForHttp, SubWorkflow) leaves when it
/// parks, and the test for whether a wake on the branch is addressed to it.
///
/// <para>The marker names the node that parked. It used to be a bare value under a key shared by every
/// node of the same kind, and was never cleared, so a second node of that kind later in the branch - or
/// the same node on its next loop lap - found the first one's marker, took itself to be resuming, and
/// continued at once on the stale "__wake" instead of parking.</para>
/// </summary>
internal static class ParkMarker
{
    private const string NodeIdProperty = "nodeId";
    private const string WaitingOnProperty = "waitingOn";

    /// <summary>Which node, if any, the wake currently on the branch was delivered to.</summary>
    internal enum WakeOwner
    {
        /// <summary>No wake payload is on the branch.</summary>
        None,

        /// <summary>The wake was delivered for this node's bookmark (or predates the node tag).</summary>
        This,

        /// <summary>The wake was delivered for another node's bookmark; this node must not consume it.</summary>
        Other
    }

    /// <summary>The keys a resumed node removes so the branch is clean for the next park.</summary>
    internal static IReadOnlyCollection<string> WakeKeys { get; } = [BookmarkResumer.WakeKey, BookmarkResumer.WakeNodeKey];

    public static JsonElement Create(Guid nodeId, string waitingOn)
    {
        return JsonSerializer.SerializeToElement(new Dictionary<string, string>
        {
            [NodeIdProperty] = nodeId.ToString(),
            [WaitingOnProperty] = waitingOn
        });
    }

    /// <summary>
    /// True when the marker under <paramref name="key"/> was left by <paramref name="nodeId"/>.
    /// <paramref name="waitingOn"/> is what the node parked on (signal name, token, child run).
    /// </summary>
    public static bool TryReadOwn(IReadOnlyDictionary<string, JsonElement> localState, string key, Guid nodeId, out string? waitingOn)
    {
        waitingOn = null;
        if (!localState.TryGetValue(key, out JsonElement marker))
        {
            return false;
        }

        // A bare string is the shape written before the marker named its node. A branch parked under
        // that shape is still waiting on the node it is pointed at, so it is honoured as this node's.
        if (marker.ValueKind == JsonValueKind.String)
        {
            waitingOn = marker.GetString();
            return true;
        }

        if (marker.ValueKind != JsonValueKind.Object
            || !marker.TryGetProperty(NodeIdProperty, out JsonElement owner)
            || owner.ValueKind != JsonValueKind.String
            || !Guid.TryParse(owner.GetString(), out Guid ownerId)
            || ownerId != nodeId)
        {
            // Another node's marker, left behind by a wait that ended without running its node again
            // (a TTL exit). This node has not parked yet.
            return false;
        }

        waitingOn = marker.TryGetProperty(WaitingOnProperty, out JsonElement on) && on.ValueKind == JsonValueKind.String
            ? on.GetString()
            : null;
        return true;
    }

    public static WakeOwner ReadWake(IReadOnlyDictionary<string, JsonElement> localState, Guid nodeId, out JsonElement wake)
    {
        if (!localState.TryGetValue(BookmarkResumer.WakeKey, out wake))
        {
            return WakeOwner.None;
        }

        // An untagged wake was delivered before the resumer named the woken node; accept it as before.
        if (!localState.TryGetValue(BookmarkResumer.WakeNodeKey, out JsonElement tag)
            || tag.ValueKind != JsonValueKind.String
            || !Guid.TryParse(tag.GetString(), out Guid wokenNodeId))
        {
            return WakeOwner.This;
        }

        return wokenNodeId == nodeId ? WakeOwner.This : WakeOwner.Other;
    }
}
