using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record WaitForHttpConfig
{
    [JsonPropertyName("ttl")]
    public required TimeSpan Ttl { get; init; }

    // Optional design-time wake token. When set, the branch parks on "http-wake:{Token}" and an
    // external caller resumes it by POSTing that token to the public wake callback - so the author
    // knows the callback URL up front and can hand it to whoever is meant to call back (a payment
    // provider, an approval webhook, ...). It is a shared secret, exactly like a webhook path:
    // for an anonymous public callback it must be high-entropy, and it must be unique per
    // concurrently-parked run (a static value shared across simultaneous runs of the same
    // definition would collide on a single bookmark key). When null the executor falls back to the
    // run's RefId, which scopes the wake per-run automatically but is NOT a secret (it also appears
    // in run-list APIs and logs) - see WaitForHttpNodeExecutor.
    [JsonPropertyName("token")]
    public string? Token { get; init; }
}
