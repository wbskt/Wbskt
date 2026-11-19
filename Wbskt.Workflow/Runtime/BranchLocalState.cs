using System.Text.Json;

namespace Wbskt.Workflow.Runtime;

/// <summary>
/// A branch's local state as the branch row stores it (a JSON object) and as nodes patch it.
/// </summary>
internal static class BranchLocalState
{
    public static IReadOnlyDictionary<string, JsonElement> Merge(
        string existingJson,
        IReadOnlyDictionary<string, JsonElement> patch,
        IReadOnlyCollection<string>? removeKeys = null)
    {
        return Merge(Deserialize(existingJson), patch, removeKeys);
    }

    /// <summary>
    /// The same merge over state already parsed, for a caller that holds it: a branch's state is up to
    /// 256 KB, and serializing it only to parse it straight back was most of a fork's cost per child.
    /// </summary>
    public static IReadOnlyDictionary<string, JsonElement> Merge(
        IReadOnlyDictionary<string, JsonElement> existing,
        IReadOnlyDictionary<string, JsonElement> patch,
        IReadOnlyCollection<string>? removeKeys = null)
    {
        Dictionary<string, JsonElement> merged = new(existing, StringComparer.Ordinal);

        // Removals run first so a node can drop a key and re-add it in the same step.
        if (removeKeys is not null)
        {
            foreach (string key in removeKeys)
            {
                merged.Remove(key);
            }
        }

        foreach (var pair in patch)
        {
            merged[pair.Key] = pair.Value.Clone();
        }

        return merged;
    }

    public static IReadOnlyDictionary<string, JsonElement> Deserialize(string json)
    {
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, EngineJson.Options)
            ?? new Dictionary<string, JsonElement>();
    }

    public static string Serialize(IReadOnlyDictionary<string, JsonElement> localState)
    {
        return JsonSerializer.Serialize(localState, EngineJson.Options);
    }
}
