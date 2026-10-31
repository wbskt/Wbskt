using System.Net;
using System.Net.Http.Json;
using System.Security;
using System.Text.Json;
using Wbskt.Client.Sdk.Models;
using Wbskt.Protocol;

namespace Wbskt.Client.Sdk.Internal;

internal sealed class AuthClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ClientConfig _config;

    public AuthClient(ClientConfig config)
    {
        _config = config;
        _httpClient = new HttpClient { BaseAddress = new Uri(config.BaseApiUrl) };
    }

    public async Task<(Guid ClientRefId, string Secret)> RegisterAsync()
    {
        var response = await _httpClient.PostAsJsonAsync("api/v1/client-registrations/initiate", new ClientRegistrationRequest(_config.PolicyPin, _config.DeviceName));

        if (!response.IsSuccessStatusCode)
        {
            throw new SecurityException("Failed to register client using PIN.");
        }

        var result = await response.Content.ReadFromJsonAsync<ClientRegistrationResponse>();
        return result != null ? (result.ClientRefId, result.Secret) : throw new InvalidOperationException("Invalid registration response.");
    }

    public async Task<DeviceToken> LoginAsync(Guid clientRefId, string secret)
    {
        var response = await _httpClient.PostAsJsonAsync("api/v1/client-auth/login", new ClientLoginRequest(clientRefId, secret));

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new DeviceLoginThrottledException(RetryAfter(response));
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized && await ErrorCodeAsync(response) == DeviceErrorCodes.Unauthorized)
        {
            throw new CredentialsRejectedException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new SecurityException("Failed to authenticate client.");
        }

        var result = await response.Content.ReadFromJsonAsync<ClientLoginResponse>();
        return result is { AccessToken.Length: > 0 }
            ? new DeviceToken(result.AccessToken, DateTimeOffset.UtcNow.AddSeconds(result.ExpiresIn))
            : throw new InvalidOperationException("Invalid login response.");
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        try
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.ValueKind == JsonValueKind.Object
                   && body.RootElement.TryGetProperty("code", out var code)
                   && code.ValueKind == JsonValueKind.String
                ? code.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta;
        }

        return header?.Date is { } date ? date - DateTimeOffset.UtcNow : null;
    }

    public void Dispose() => _httpClient.Dispose();
}
