using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Wbskt.Client.Sdk.Models;
using Wbskt.Client.Windows.Models;

namespace Wbskt.Client.Windows.Service.Engine;

public static partial class CapabilityDiscovery
{
    private static readonly Regex VariableRegex = VariableRegexGen();

    public static ClientCapabilities BuildCapabilities(List<CommandMapping> mappings)
    {
        var commands = new List<CommandCapability>();

        foreach (var mapping in mappings.Where(m => m.IsEnabled))
        {
            var parameters = new HashSet<string>();

            // Extract all {{payload.VAR}} from all parameter values
            foreach (var paramValue in mapping.Parameters.Values)
            {
                var matches = VariableRegex.Matches(paramValue);
                foreach (Match match in matches)
                {
                    parameters.Add(match.Groups[1].Value);
                }
            }

            var propSchemas = parameters.Select(p => new PropertySchema(
                Name: p,
                Label: PrettifyName(p),
                DataType: "string", // Default to string for auto-discovered
                Description: $"Auto-discovered parameter: {p}",
                IsRequired: true
            )).ToList();

            commands.Add(new CommandCapability(
                Command: mapping.CommandName,
                Description: string.IsNullOrEmpty(mapping.FriendlyName) ? mapping.ActionType.ToString() : mapping.FriendlyName,
                Parameters: propSchemas
            ));
        }

        return new ClientCapabilities(
            Agent: "Wbskt.Windows.Edge",
            Version: "1.0.0",
            OS: RuntimeInformation.OSDescription,
            Capabilities: commands
        );
    }

    private static string PrettifyName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        // simple snake_case or camelCase to Title Case
        var result = CaseRegexGen().Replace(name, "$1 $2");
        result = result.Replace("_", " ");
        return char.ToUpper(result[0]) + result[1..];
    }

    [GeneratedRegex(@"\{\{payload\.(.+?)\}\}", RegexOptions.Compiled)]
    private static partial Regex VariableRegexGen();
    [GeneratedRegex("([a-z])([A-Z])")]
    private static partial Regex CaseRegexGen();
}
