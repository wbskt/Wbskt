using System.IO;
using System.Text.Json;

namespace Wbskt.Client.Windows.Models;

public class ConfigurationStore
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
        "Wbskt", 
        "config.json"
    );

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
        "Wbskt", 
        "settings.json"
    );

    public List<CommandMapping> LoadMappings()
    {
        if (!File.Exists(ConfigPath)) return new List<CommandMapping>();

        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<List<CommandMapping>>(json) ?? new List<CommandMapping>();
        }
        catch
        {
            return new List<CommandMapping>();
        }
    }

    public void SaveMappings(List<CommandMapping> mappings)
    {
        var dir = Path.GetDirectoryName(ConfigPath);
        if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(mappings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }

    public ClientSettings? LoadSettings()
    {
        if (!File.Exists(SettingsPath)) return null;

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<ClientSettings>(json);
        }
        catch
        {
            return null;
        }
    }

    public void SaveSettings(ClientSettings settings)
    {
        var dir = Path.GetDirectoryName(SettingsPath);
        if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }
}
