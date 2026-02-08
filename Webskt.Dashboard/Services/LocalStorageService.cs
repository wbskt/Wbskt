using Microsoft.JSInterop;

namespace Webskt.Dashboard.Services;

public sealed class LocalStorageService
{
    private readonly IJSRuntime _js;

    public LocalStorageService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task SetItemAsync<T>(string key, T value)
    {
        await _js.InvokeVoidAsync("localStorage.setItem", key, System.Text.Json.JsonSerializer.Serialize(value));
    }

    public async Task<T?> GetItemAsync<T>(string key)
    {
        var json = await _js.InvokeAsync<string?>("localStorage.getItem", key);
        return json == null ? default : System.Text.Json.JsonSerializer.Deserialize<T>(json);
    }

    public async Task RemoveItemAsync(string key)
    {
        await _js.InvokeVoidAsync("localStorage.removeItem", key);
    }
}
