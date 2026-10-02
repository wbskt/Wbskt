using System.Text.Json;

namespace Wbskt.Workflow.NodeExecutors.Controls;

/// <summary>
/// The join token a branch carries names the cohort it will contribute to at the next Join. Cohorts
/// nest - a Fork inside a ParallelForEach body opens an inner cohort whose Join must hand the branch
/// back to the outer one - so the tokens of the enclosing cohorts ride along as a stack. Without it
/// the inner Join's winner would still carry the inner token, contribute to the inner aggregator a
/// second time at the outer Join, and the outer cohort would never converge.
/// </summary>
internal static class JoinTokens
{
    public const string TokenKey = "__join_token";
    public const string OuterKey = "__join_outer";

    /// <summary>The state a child of a new cohort starts with: the new token, with the branch's
    /// current token (if any) pushed onto the stack of enclosing ones.</summary>
    public static Dictionary<string, JsonElement> ForChild(
        IReadOnlyDictionary<string, JsonElement> parentState, JsonElement newToken)
    {
        List<string> outer = ReadOuter(parentState);
        if (parentState.TryGetValue(TokenKey, out JsonElement current) && current.ValueKind == JsonValueKind.String)
        {
            outer.Add(current.GetString()!);
        }

        var state = new Dictionary<string, JsonElement> { [TokenKey] = newToken };
        if (outer.Count > 0)
        {
            state[OuterKey] = JsonSerializer.SerializeToElement(outer);
        }

        return state;
    }

    /// <summary>
    /// What the winner of a Join carries on: the enclosing cohort's token, popped off the stack. At
    /// the outermost level there is nothing to restore and the state is left as it is.
    /// </summary>
    public static (Dictionary<string, JsonElement> Patch, List<string> RemoveKeys) AfterJoin(
        IReadOnlyDictionary<string, JsonElement> state)
    {
        var patch = new Dictionary<string, JsonElement>();
        var remove = new List<string>();

        List<string> outer = ReadOuter(state);
        if (outer.Count == 0)
        {
            return (patch, remove);
        }

        patch[TokenKey] = JsonSerializer.SerializeToElement(outer[^1]);
        outer.RemoveAt(outer.Count - 1);
        if (outer.Count > 0)
        {
            patch[OuterKey] = JsonSerializer.SerializeToElement(outer);
        }
        else
        {
            remove.Add(OuterKey);
        }

        return (patch, remove);
    }

    private static List<string> ReadOuter(IReadOnlyDictionary<string, JsonElement> state)
    {
        if (!state.TryGetValue(OuterKey, out JsonElement element) || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return element.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToList();
    }
}
