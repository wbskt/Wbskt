using System.Net.Http.Json;
using System.Security;
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

    public async Task<string> LoginAsync(Guid clientRefId, string secret)
    {
        var response = await _httpClient.PostAsJsonAsync("api/v1/client-auth/login", new ClientLoginRequest(clientRefId, secret));

        if (!response.IsSuccessStatusCode)
        {
            throw new SecurityException("Failed to authenticate client.");
        }

        var result = await response.Content.ReadFromJsonAsync<ClientLoginResponse>();
        return result?.AccessToken ?? throw new InvalidOperationException("Invalid login response.");
    }

    public void Dispose() => _httpClient.Dispose();
}
