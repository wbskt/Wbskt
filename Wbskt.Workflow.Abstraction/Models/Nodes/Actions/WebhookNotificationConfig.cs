using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record WebhookNotificationConfig
{
    /// <summary>
    /// The longest header list a step may set. Generous for any real API, and it keeps a definition
    /// from carrying an arbitrarily large request.
    /// </summary>
    public const int MaxHeaders = 20;

    /// <summary>How long a call waits for the whole response when the step names no timeout.</summary>
    public const int DefaultTimeoutSeconds = 30;

    /// <summary>
    /// The longest a step may wait. A run's branch holds its place while it waits, so this is kept
    /// well under the engine's own limits.
    /// </summary>
    public const int MaxTimeoutSeconds = 60;

    /// <summary>
    /// The most of a response body the step reads. The body becomes the step's output and is stored
    /// with the run, so an endpoint cannot make a run carry an arbitrarily large state.
    /// </summary>
    public const int MaxResponseBytes = 256 * 1024;

    /// <summary>
    /// Optional: an HTTP-auth integration whose credential is added to the request. With one, the URL
    /// may be relative to the integration's base URL, and an absolute URL must share its origin, so
    /// the credential only ever goes where its owner pointed it.
    /// </summary>
    [JsonPropertyName("integrationRef")]
    public Guid? IntegrationRef { get; init; }

    /// <summary>Extra request headers, by name. Never a credential: that belongs in an integration.</summary>
    [JsonPropertyName("headers")]
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    [JsonPropertyName("url")]
    public required string Url { get; init; }

    [JsonPropertyName("method")]
    public required string Method { get; init; }

    [JsonPropertyName("body")]
    public System.Text.Json.JsonElement? Body { get; init; } = null;

    /// <summary>
    /// Optional: seconds to wait for the response, 1 to <see cref="MaxTimeoutSeconds"/>;
    /// <see cref="DefaultTimeoutSeconds"/> when unset.
    /// </summary>
    [JsonPropertyName("timeoutSeconds")]
    public int? TimeoutSeconds { get; init; }

}

