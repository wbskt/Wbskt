using System.Text.Json;
using System.Text.RegularExpressions;

namespace Wbskt.Client.Windows.Service.Engine;

public static class VariableResolver
{
    public static string Resolve(string template, string jsonPayload)
    {
        if (string.IsNullOrEmpty(template) || !template.Contains("{{"))
        {
            return template;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonPayload);
            var root = doc.RootElement;

            return Regex.Replace(template, @"\{\{payload\.(.+?)\}\}", m =>
            {
                var path = m.Groups[1].Value;
                return root.TryGetProperty(path, out var prop) ? prop.ToString() : m.Value;
            });
        }
        catch
        {
            return template;
        }
    }
}
