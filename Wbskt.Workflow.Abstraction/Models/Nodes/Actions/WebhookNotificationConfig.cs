using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record WebhookNotificationConfig
{
    /// <summary>
    /// The longest header list a step may set. Generous for any real API, and it keeps a definition
    /// from carrying an arbitrarily large request.
    /// </summary>
    public const int MaxHeaders = 20;

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

}

