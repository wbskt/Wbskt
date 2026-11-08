using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wbskt.Management.Models.Workflow;

[JsonConverter(typeof(JsonStringEnumConverter<TestRunSource>))]
public enum TestRunSource
{
    [JsonStringEnumMemberName("draft")]
    Draft,

    [JsonStringEnumMemberName("live")]
    Live
}

/// <summary>
/// Starts a test run of the workflow's draft or live version. <c>Payload</c> is what the run reads as
/// <c>trigger</c>, in the trigger's own shape. <c>TriggerNodeId</c> may be left out when the workflow has
/// one trigger.
/// </summary>
public record StartTestRunRequest(TestRunSource Source, Dictionary<string, JsonElement>? Payload = null, Guid? TriggerNodeId = null);

public record StartTestRunResponse(Guid RunRefId);
