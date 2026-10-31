using System.Text.Json;

namespace Wbskt.Client.Sdk;

/// <summary>
/// Keeps the device's credentials in a JSON file, so it registers once and signs in with them on
/// every later start. The default for <see cref="WbsktClient(string, string)"/>.
/// </summary>
public sealed class FileClientStorage : IClientStorage
{
    private readonly string _filePath;

    public FileClientStorage(string filePath)
    {
        _filePath = filePath;
    }

    /// <summary>
    /// The file for a device named <paramref name="deviceName"/>: <c>wbskt/{name}.json</c> in the
    /// user's local application data folder (<c>~/.local/share</c> on Linux), or the working
    /// directory where there is none. Named per device, so several on one machine don't share one.
    /// </summary>
    public static FileClientStorage ForDevice(string deviceName)
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
        if (string.IsNullOrEmpty(root))
        {
            root = Directory.GetCurrentDirectory();
        }

        string safeName = string.Concat(deviceName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return new FileClientStorage(Path.Combine(root, "wbskt", $"{safeName}.json"));
    }

    public async Task SaveCredentialsAsync(Guid refId, string secret)
    {
        string? directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(new { RefId = refId, Secret = secret });
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
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
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
