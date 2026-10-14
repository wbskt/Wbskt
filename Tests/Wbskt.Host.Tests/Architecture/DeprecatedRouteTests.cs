using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;

namespace Wbskt.Host.Tests.Architecture;

/// <summary>
/// A route kept for one release after its replacement shipped stays out of the API description
/// (<c>[ApiExplorerSettings(IgnoreApi = true)]</c>), so the typed client and the OpenAPI document do
/// not grow a call that is about to go, and its summary starts with <c>Deprecated:</c> and names the
/// replacement. Read both ways: a deprecated action is hidden, and a hidden action is deprecated.
/// </summary>
/// <remarks>
/// "Deprecated" means a summary starting with <c>Deprecated:</c> or an <see cref="ObsoleteAttribute"/>.
/// Summaries come from each host's XML documentation file (<c>GenerateDocumentationFile</c> is on for
/// every host). Only <c>IgnoreApi</c> on the action counts here: a whole controller hidden from the
/// description (the internal and device-facing ones) is a different decision, not a deprecation.
/// </remarks>
public sealed partial class DeprecatedRouteTests
{
    private static readonly Assembly[] Hosts =
    [
        typeof(global::Wbskt.Auth.Host.Program).Assembly,
        typeof(global::Wbskt.Management.Host.Program).Assembly,
        typeof(global::Wbskt.Socket.Host.Program).Assembly,
        typeof(global::Wbskt.Workflow.Engine.Host.Program).Assembly,
    ];

    private const string Marker = "Deprecated:";

    [Fact]
    public void A_deprecated_action_is_hidden_and_a_hidden_action_is_deprecated()
    {
        var deprecated = new List<string>();
        var offending = new List<string>();
        foreach (var host in Hosts)
        {
            var summaries = Summaries(host);
            foreach (var action in HostArchitectureTests.ActionsIn(host).DistinctBy(a => a.Method))
            {
                string? summary = SummaryOf(action.Method, summaries);
                bool saysDeprecated = summary?.StartsWith(Marker, StringComparison.Ordinal) == true;
                bool obsolete = action.Method.GetCustomAttribute<ObsoleteAttribute>() is not null;
                bool hidden = action.Method.GetCustomAttribute<ApiExplorerSettingsAttribute>()?.IgnoreApi == true;

                if (saysDeprecated || obsolete)
                {
                    deprecated.Add(action.Name);
                    if (!hidden)
                    {
                        offending.Add($"{action.Name} is deprecated but still described: add [ApiExplorerSettings(IgnoreApi = true)]");
                    }
                }

                if (hidden && !saysDeprecated)
                {
                    offending.Add($"{action.Name} is hidden from the API description: its <summary> must start with \"{Marker}\" and name the replacement");
                }
            }
        }

        // The rule reads what it finds, so pin that it finds the routes deprecated when it arrived.
        Assert.Contains("ClientsController.GetByPolicy", deprecated);
        Assert.Contains("ClientsController.UpdateStatus", deprecated);

        Assert.True(offending.Count == 0, string.Join(Environment.NewLine, offending.Order(StringComparer.Ordinal)));
    }

    /// <summary>Member documentation ID to its summary text, whitespace collapsed.</summary>
    private static Dictionary<string, string> Summaries(Assembly host)
    {
        string name = host.GetName().Name!;
        string? path = DocFileCandidates(host, name).FirstOrDefault(File.Exists);
        Assert.True(path is not null, $"{name}.xml was not found next to the host or under Hosts/{name}/bin; GenerateDocumentationFile must stay on for the hosts.");

        return XDocument.Load(path!)
            .Descendants("member")
            .Select(m => (Id: m.Attribute("name")?.Value, Summary: m.Element("summary")?.Value))
            .Where(m => m.Id is not null && m.Summary is not null)
            .GroupBy(m => m.Id!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => Whitespace().Replace(g.First().Summary!, " ").Trim(), StringComparer.Ordinal);
    }

    private static IEnumerable<string> DocFileCandidates(Assembly host, string name)
    {
        // A referenced project's documentation file is copied next to its assembly in this test's
        // output; the host's own build output is the fallback.
        yield return Path.ChangeExtension(host.Location, ".xml");

        string bin = Path.Combine(RepoPaths.Root(), "Hosts", name, "bin");
        if (Directory.Exists(bin))
        {
            foreach (string file in Directory.EnumerateFiles(bin, name + ".xml", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                yield return file;
            }
        }
    }

    private static string? SummaryOf(MethodInfo method, Dictionary<string, string> summaries)
    {
        string prefix = $"M:{TypeId(method.DeclaringType!)}.{method.Name}";
        var parameters = method.GetParameters();
        string id = parameters.Length == 0 ? prefix : $"{prefix}({string.Join(",", parameters.Select(p => TypeId(p.ParameterType)))})";
        if (summaries.TryGetValue(id, out string? summary))
        {
            return summary;
        }

        // A signature this does not spell exactly as the compiler does: fall back to the name when
        // it is not overloaded.
        var byName = summaries.Where(s => s.Key == prefix || s.Key.StartsWith(prefix + "(", StringComparison.Ordinal)).ToList();
        return byName.Count == 1 ? byName[0].Value : null;
    }

    /// <summary>A type as documentation IDs spell it: <c>System.Nullable{System.Guid}</c>, <c>A.B.C</c> for a nested type.</summary>
    private static string TypeId(Type type)
    {
        if (type.IsByRef)
        {
            return TypeId(type.GetElementType()!) + "@";
        }

        if (type.IsArray)
        {
            return TypeId(type.GetElementType()!) + "[]";
        }

        if (type.IsGenericType)
        {
            string definition = type.GetGenericTypeDefinition().FullName!;
            int tick = definition.IndexOf('`');
            if (tick >= 0)
            {
                definition = definition[..tick];
            }

            return $"{definition.Replace('+', '.')}{{{string.Join(",", type.GetGenericArguments().Select(TypeId))}}}";
        }

        return (type.FullName ?? type.Name).Replace('+', '.');
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
