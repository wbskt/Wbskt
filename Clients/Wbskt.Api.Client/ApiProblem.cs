using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wbskt.Api.Client;

/// <summary>
/// The error body every Wbskt host answers with: RFC 9457 Problem Details, with the application's
/// <see cref="Code"/> (<c>AUTH_ROLE_CONFLICT</c>, <c>CLIENT_NOT_FOUND</c>, ...) and the request's
/// <see cref="TraceId"/> as extensions. <see cref="Errors"/> is set on a validation failure
/// (<c>VALIDATION_FAILED</c>) and names each field that was refused. A refusal by a plan limit carries
/// <see cref="Plan"/>, <see cref="Limit"/> and <see cref="Current"/>; any other extension a host adds is in
/// <see cref="Extensions"/>.
/// </summary>
public sealed record ApiProblem
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string? Type { get; init; }

    public string? Title { get; init; }

    public int? Status { get; init; }

    public string? Detail { get; init; }

    public string? Instance { get; init; }

    /// <summary>The application's error code: what a caller branches on.</summary>
    public string? Code { get; init; }

    /// <summary>Ties the response to the host's log lines.</summary>
    public string? TraceId { get; init; }

    /// <summary>For a validation failure, the messages for each field that was refused.</summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }

    /// <summary>For a plan limit, the plan that set it.</summary>
    public string? Plan { get; init; }

    /// <summary>For a plan limit, how many the plan allows.</summary>
    public long? Limit { get; init; }

    /// <summary>For a plan limit, how many there are now.</summary>
    public long? Current { get; init; }

    /// <summary>Any other member of the body, such as an integration's <c>usedBy</c>.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }

    /// <summary>
    /// Reads the problem off a response, or returns null when the body is empty or not a problem.
    /// Never throws for a body it cannot read: the status carries the failure either way.
    /// </summary>
    public static async Task<ApiProblem?> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            var problem = JsonSerializer.Deserialize<ApiProblem>(text, Json);
            return problem is null or { Code: null, Title: null, Detail: null, Status: null } ? null : problem;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
