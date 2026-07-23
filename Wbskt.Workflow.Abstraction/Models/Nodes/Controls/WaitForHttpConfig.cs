using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record WaitForHttpConfig
{
    [JsonPropertyName("ttl")]
    public required TimeSpan Ttl { get; init; }

    // Required design-time wake token. The branch parks on "http-wake:{Token}" and an external
    // caller resumes it by POSTing that token to the public wake callback - so the author knows the
    // callback URL up front and can hand it to whoever is meant to call back (a payment provider, an
    // approval webhook, ...). It is the ONLY thing gating that anonymous public callback, so it is a
    // shared secret: WorkflowValidator requires it to be present and high-entropy at publish time,
    // and it must be unique per concurrently-parked run (a static value shared across simultaneous
    // runs of the same definition would collide on a single bookmark key). There is deliberately no
    // fallback to the run's RefId, which is not secret (it appears in run-list APIs and logs).
    // Nullable only so malformed definitions surface as a validation error rather than a
    // deserialization failure - see WaitForHttpNodeExecutor and WorkflowValidator.
    [JsonPropertyName("token")]
    public string? Token { get; init; }
}
