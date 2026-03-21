using System.Text.Json;

namespace Wbskt.Client.Windows.Models;

public static class ConfigurationStore
{    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
        "Wbskt", 
        "config.json"
    );

    public static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
        "Wbskt", 
        "settings.json"
    );

    public static List<CommandMapping> LoadMappings()
    {
        if (!File.Exists(ConfigPath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var dtos = JsonSerializer.Deserialize<List<CommandMappingDto>>(json, JsonOptions);
            return dtos?.Select(d => d.ToModel()).ToList() ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return [];
        }
    }

    public static void SaveMappings(List<CommandMapping> mappings)
    {
        var dir = Path.GetDirectoryName(ConfigPath);
        if (dir != null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var dtos = mappings.Select(m => m.ToDto()).ToList();
        var json = JsonSerializer.Serialize(dtos, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }

    public static ClientSettings LoadSettings()
    {
        if (!File.Exists(SettingsPath))
        {
            return new ClientSettings(
                "https://localhost:7010",
                "wss://localhost:7020",
                Environment.MachineName
            );
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<ClientSettings>(json) ?? new ClientSettings(
                "https://localhost:7010",
                "wss://localhost:7020",
                Environment.MachineName
            );
        }
        catch
        {
            return new ClientSettings(
                "https://localhost:7010",
                "wss://localhost:7020",
                Environment.MachineName
            );
        }
    }

    public static void SaveSettings(ClientSettings settings)
    {
        var dir = Path.GetDirectoryName(SettingsPath);
        if (dir != null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }
}
