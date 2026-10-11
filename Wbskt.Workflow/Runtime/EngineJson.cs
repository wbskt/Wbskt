using System.Text.Json;

namespace Wbskt.Workflow.Runtime;

/// <summary>The serializer settings for everything the engine stores as JSON: web defaults, one instance.</summary>
internal static class EngineJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
