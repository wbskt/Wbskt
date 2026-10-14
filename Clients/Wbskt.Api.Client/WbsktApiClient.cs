using System.Collections;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wbskt.Api.Client;

/// <summary>
/// What the typed clients share: one <see cref="HttpClient"/>, the host's address, the bearer token,
/// and how a response is read. Every endpoint method comes in two forms: <c>XAsync</c> returns the
/// contract type and throws <see cref="ApiException"/> (with the parsed <see cref="ApiProblem"/>) on a
/// failure status; <c>XRawAsync</c> returns the <see cref="HttpResponseMessage"/> untouched, for a
/// caller that asserts on the status, the error code or a header.
/// </summary>
/// <remarks>
/// A client holds no state beyond its token, so <c>As(token)</c> makes a sibling for another caller
/// over the same <see cref="HttpClient"/>; that is how one test process speaks for many users.
/// </remarks>
public abstract class WbsktApiClient
{
    /// <summary>What a request body is written with: web defaults, enums as numbers, as the hosts read them.</summary>
    public static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web);

    /// <summary>What a response body is read with. An enum is read whether a host writes it as a name or a number.</summary>
    public static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>The body an endpoint that wants some JSON object gets when the caller has none.</summary>
    protected static readonly object EmptyBody = new Dictionary<string, object>();

    protected WbsktApiClient(HttpClient http, Uri? baseAddress, string? accessToken)
    {
        ArgumentNullException.ThrowIfNull(http);

        Http = http;
        BaseAddress = baseAddress ?? http.BaseAddress
            ?? throw new ArgumentException("Pass the host's address, or give the HttpClient a BaseAddress.", nameof(baseAddress));
        AccessToken = accessToken;
    }

    /// <summary>The host this client speaks to, e.g. <c>https://auth.example.com</c>.</summary>
    public Uri BaseAddress { get; }

    /// <summary>The bearer token sent with every request, or null to call anonymously.</summary>
    public string? AccessToken { get; set; }

    protected HttpClient Http { get; }

    /// <summary>
    /// Sends any request and returns the response untouched. <paramref name="path"/> is relative to
    /// <see cref="BaseAddress"/>, or an absolute <c>http(s)://</c> URL for another host. A null
    /// <paramref name="body"/> sends no content; anything else is written as JSON, so a caller can send
    /// a shape the contract does not allow and see it refused.
    /// </summary>
    public async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, object? body = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(path);

        using var request = new HttpRequestMessage(method, Resolve(path));
        if (AccessToken is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: WriteOptions);
        }

        return await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Throws <see cref="ApiException"/>, carrying the parsed problem, unless the response is a success.</summary>
    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var problem = await ApiProblem.ReadAsync(response, cancellationToken).ConfigureAwait(false);
        throw new ApiException(response.StatusCode, problem, response.RequestMessage?.Method, response.RequestMessage?.RequestUri);
    }

    /// <summary>Reads a successful response's body as <typeparamref name="T"/>; a failure throws <see cref="ApiException"/>.</summary>
    public static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        return await response.Content.ReadFromJsonAsync<T>(ReadOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath} answered {(int)response.StatusCode} with an empty body.");
    }

    protected static async Task<T> ReceiveAsync<T>(Task<HttpResponseMessage> sending, CancellationToken cancellationToken)
    {
        using var response = await sending.ConfigureAwait(false);
        return await ReadAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    protected static async Task ReceiveAsync(Task<HttpResponseMessage> sending, CancellationToken cancellationToken)
    {
        using var response = await sending.ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    protected static async Task<string> ReceiveTextAsync(Task<HttpResponseMessage> sending, CancellationToken cancellationToken)
    {
        using var response = await sending.ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Escapes a value for one path segment.</summary>
    protected static string E(string value) => Uri.EscapeDataString(value);

    /// <summary>
    /// A query string (with its leading <c>?</c>) from the parameters that have a value, or an empty
    /// string. Times are written round-trip, enums by name, lists comma-separated.
    /// </summary>
    protected static string Query(params (string Name, object? Value)[] parameters)
    {
        var query = new StringBuilder();
        foreach (var (name, value) in parameters)
        {
            if (Format(value) is not { } text)
            {
                continue;
            }

            query.Append(query.Length == 0 ? '?' : '&').Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(text));
        }

        return query.ToString();
    }

    private static string? Format(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? "true" : "false",
        DateTimeOffset moment => moment.ToString("O", CultureInfo.InvariantCulture),
        DateTime moment => moment.ToString("O", CultureInfo.InvariantCulture),
        Enum member => member.ToString(),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        IEnumerable items => FormatList(items),
        _ => value.ToString()
    };

    private static string? FormatList(IEnumerable items)
    {
        string joined = string.Join(",", items.Cast<object?>().Select(Format).Where(item => item is not null));
        return joined.Length > 0 ? joined : null;
    }

    private Uri Resolve(string path) =>
        path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? new Uri(path, UriKind.Absolute)
            : new Uri($"{BaseAddress.AbsoluteUri.TrimEnd('/')}/{path.TrimStart('/')}", UriKind.Absolute);
}
