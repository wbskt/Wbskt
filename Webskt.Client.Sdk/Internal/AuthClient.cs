using System.Net.Http.Json;
using System.Security;
using Webskt.Client.Sdk.Models;

namespace Webskt.Client.Sdk.Internal;

internal sealed class AuthClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ClientConfig _config;

    public AuthClient(ClientConfig config)
    {
        _config = config;
        _httpClient = new HttpClient { BaseAddress = new Uri(config.BaseApiUrl) };
    }

    public async Task<(Guid RefId, string Secret)> RegisterAsync()
    {
        var response = await _httpClient.PostAsJsonAsync("api/client-registrations/initiate", new
        {
            Name = _config.DeviceName,
            Pin = _config.PolicyPin
        });

        if (!response.IsSuccessStatusCode)
        {
            throw new SecurityException("Failed to register client using PIN.");
        }

        var result = await response.Content.ReadFromJsonAsync<RegistrationResult>();
        return result != null ? (result.RefId, result.Secret) : throw new InvalidOperationException("Invalid registration response.");
    }

    public async Task<string> LoginAsync(Guid clientRefId, string secret)
    {
        var response = await _httpClient.PostAsJsonAsync("api/client-auth/login", new
        {
            ClientRefId = clientRefId,
            Secret = secret
        });

        if (!response.IsSuccessStatusCode)
        {
            throw new SecurityException("Failed to authenticate client.");
        }

        var result = await response.Content.ReadFromJsonAsync<LoginResult>();
        return result?.AccessToken ?? throw new InvalidOperationException("Invalid login response.");
    }

    public void Dispose() => _httpClient.Dispose();

    private record RegistrationResult(Guid RefId, string Secret);
    private record LoginResult(string AccessToken);
}
