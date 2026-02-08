using System.Net.Http.Json;
using Webskt.Common.Abstraction.Models.Auth;

namespace Webskt.Dashboard.Services;

public sealed class AuthService
{
    private readonly HttpClient _http;
    private readonly LocalStorageService _localStorage;
    private const string TokenKey = "wbskt_token";

    public AuthService(HttpClient http, LocalStorageService localStorage)
    {
        _http = http;
        _localStorage = localStorage;
    }

    public async Task<bool> LoginAsync(string email, string password)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("https://localhost:7000/api/Auth/login", new LoginRequest(email, password));
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
                if (result != null)
                {
                    await _localStorage.SetItemAsync(TokenKey, result.AccessToken);
                    return true;
                }
            }
        }
        catch
        {
            // Log error
        }
        
        return false;
    }

    public async Task LogoutAsync()
    {
        await _localStorage.RemoveItemAsync(TokenKey);
    }

    public async Task<string?> GetTokenAsync()
    {
        return await _localStorage.GetItemAsync<string>(TokenKey);
    }

    public async Task<bool> IsAuthenticatedAsync()
    {
        var token = await GetTokenAsync();
        return !string.IsNullOrEmpty(token);
    }
}