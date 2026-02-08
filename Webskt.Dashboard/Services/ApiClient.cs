using System.Net.Http.Headers;
using System.Net.Http.Json;
using Webskt.Common.Abstraction.Models.Management;

namespace Webskt.Dashboard.Services;

public sealed class ApiClient
{
    private readonly HttpClient _http;
    private readonly AuthService _auth;

    public ApiClient(HttpClient http, AuthService auth)
    {
        _http = http;
        _auth = auth;
    }

    private async Task SetAuthHeader()
    {
        var token = await _auth.GetTokenAsync();
        if (!string.IsNullOrEmpty(token))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    public async Task<IReadOnlyCollection<RegistrationPolicyResponse>> GetPoliciesAsync()
    {
        await SetAuthHeader();
        return await _http.GetFromJsonAsync<IReadOnlyCollection<RegistrationPolicyResponse>>("https://localhost:7010/api/registration-policies") 
               ?? Array.Empty<RegistrationPolicyResponse>();
    }

    public async Task<RegistrationPolicyResponse> CreatePolicyAsync(RegistrationPolicyRequest request)
    {
        await SetAuthHeader();
        var response = await _http.PostAsJsonAsync("https://localhost:7010/api/registration-policies", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RegistrationPolicyResponse>() 
               ?? throw new InvalidOperationException("Failed to create policy.");
    }

    public async Task<IReadOnlyCollection<ClientResponse>> GetClientsAsync()
    {
        await SetAuthHeader();
        return await _http.GetFromJsonAsync<IReadOnlyCollection<ClientResponse>>("https://localhost:7010/api/clients")
               ?? Array.Empty<ClientResponse>();
    }
}
