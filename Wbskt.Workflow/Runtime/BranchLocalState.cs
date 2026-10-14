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
        Dictionary<string, JsonElement> merged = new(Deserialize(existingJson), StringComparer.Ordinal);

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
