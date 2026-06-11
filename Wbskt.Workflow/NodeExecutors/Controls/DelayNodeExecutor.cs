using System.Globalization;
using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

/// <summary>
/// Durable delay. On first visit it parks the branch with a <see cref="TimerWakeCondition"/>
/// at now + duration (the BookmarkScheduler resumes the branch when the timer is due). On the
/// resume visit it continues. First-visit vs resume is distinguished by a local-state marker,
/// because the engine re-executes the same node when a bookmark fires.
/// </summary>
internal sealed class DelayNodeExecutor(IClock clock) : INodeExecutor
{
    internal const string DelayUntilKey = "__delay_until";

    public string Kind => NodeKind.ControlDelay;

    public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        _ = ct;
        var node = (DelayNode)ctx.Node;

        // A marker from a previous visit means this is a resume (the timer bookmark fired).
        if (ctx.Branch.LocalState.TryGetValue(DelayUntilKey, out JsonElement until)
            && until.ValueKind == JsonValueKind.String
            && DateTime.TryParse(until.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime wakeAt))
        {
            if (clock.UtcNow >= wakeAt)
            {
                return Continue();
            }

            // Re-dispatched before the timer was actually due: re-park on the same instant.
            return Park(wakeAt, includeMarker: false);
        }

        TimeSpan duration = node.Config?.Duration ?? TimeSpan.Zero;
        if (duration <= TimeSpan.Zero)
        {
            return Continue();
        }

        return Park(clock.UtcNow + duration, includeMarker: true);
    }

    private static Task<NodeExecutionResult> Continue()
    {
        return Task.FromResult<NodeExecutionResult>(
            new NodeExecutionResult.Continue("default", new Dictionary<string, JsonElement>()));
    }

    private static Task<NodeExecutionResult> Park(DateTime wakeAt, bool includeMarker)
    {
        Dictionary<string, JsonElement> patch = includeMarker
            ? new Dictionary<string, JsonElement> { [DelayUntilKey] = JsonSerializer.SerializeToElement(wakeAt.ToString("O")) }
            : new Dictionary<string, JsonElement>();

        return Task.FromResult<NodeExecutionResult>(
            new NodeExecutionResult.WaitForBookmark(new TimerWakeCondition(wakeAt), patch));
    }
}
