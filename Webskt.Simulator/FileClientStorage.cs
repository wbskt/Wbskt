using System.Text.Json;
using Webskt.Client.Sdk;

namespace Webskt.Simulator;

public sealed class FileClientStorage : IClientStorage
{
    private readonly string _filePath;

    public FileClientStorage(string filePath = "wbskt_creds.json")
    {
        _filePath = filePath;
    }

    public async Task SaveCredentialsAsync(Guid refId, string secret)
    {
        var data = new { RefId = refId, Secret = secret };
        var json = JsonSerializer.Serialize(data);
        await File.WriteAllTextAsync(_filePath, json);
    }

    public async Task<(Guid? RefId, string? Secret)> LoadCredentialsAsync()
    {
        if (!File.Exists(_filePath))
        {
            return (null, null);
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            using var doc = JsonDocument.Parse(json);
            var refId = doc.RootElement.GetProperty("RefId").GetGuid();
            var secret = doc.RootElement.GetProperty("Secret").GetString();
            return (refId, secret);
        }
        catch
        {
            return (null, null);
        }
    }

    public Task ClearCredentialsAsync()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
        return Task.CompletedTask;
    }
}
