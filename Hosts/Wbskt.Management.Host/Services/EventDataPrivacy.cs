using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Wbskt.Events;

namespace Wbskt.Management.Host.Services;

/// <summary>
/// The public form of an event's JSON, as the event log stores and returns it: without the properties
/// <see cref="SignalRPrivateAttribute"/> marks (internal ids, the caller's address and user agent), the
/// same ones the hub hides. Each has a column of its own, so nothing is lost by dropping it from the body.
/// </summary>
internal static class EventDataPrivacy
{
    public static readonly IReadOnlySet<string> Names = BuildNames();

    /// <summary>The JSON without any private property, in either casing. Most bodies hold none and are returned as they are.</summary>
    public static string Strip(string json)
    {
        var present = Names.Where(n => json.Contains($"\"{n}\"", StringComparison.OrdinalIgnoreCase)).ToArray();
        return present.Length == 0 ? json : Without(json, present);
    }

    /// <summary>The event's JSON without the named properties, matched in either casing.</summary>
    internal static string Without(string json, params string[] names)
    {
        if (JsonNode.Parse(json) is not JsonObject body)
        {
            return json;
        }

        foreach (var key in body.Select(p => p.Key).Where(k => names.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
        {
            body.Remove(key);
        }

        return body.ToJsonString();
    }

    private static HashSet<string> BuildNames()
    {
        var names = typeof(SignalRPrivateAttribute).Assembly.GetTypes()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(p => p.IsDefined(typeof(SignalRPrivateAttribute), false))
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A sign-in names the caller's address ipAddress; the logger moves it to the ClientAddress column.
        names.Add("ipAddress");
        return names;
    }
}
