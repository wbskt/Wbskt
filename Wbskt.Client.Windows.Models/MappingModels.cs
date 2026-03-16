namespace Wbskt.Client.Windows.Models;

public enum ActionType
{
    Toast,              // Windows Notifications
    ProcessStart,       // Launching .exe
    ProcessKill,        // Closing an app
    PowerShell,         // Running .ps1 scripts
    Python,             // Running .py scripts
    VolumeControl,      // Set/Mute audio
    MonitorControl,     // Turn on/off
    SystemPower,        // Lock, Sleep, Restart
    KeySimulation,      // Virtual keypresses
    OpenFile            // Open a URL or specific file
}

public enum ParameterInputType
{
    Text,
    TextArea,
    FilePath,
    Number,
    Dropdown,
    Password
}

public record ParameterDefinition(
    string Key,
    string Label,
    ParameterInputType InputType,
    bool IsRequired = false,
    string? Placeholder = null,
    string? DefaultValue = null,
    List<string>? Options = null // For Dropdown
);

public record ActionDefinition(
    ActionType Type,
    string DisplayName,
    string Description,
    List<ParameterDefinition> ParameterSchema
);

public interface IActionHandler
{
    ActionType Type { get; }
    Task ExecuteAsync(Dictionary<string, string> resolvedParameters);
}

public static class ActionRegistry
{
    public static readonly List<ActionDefinition> Definitions = new()
    {
        new ActionDefinition(
            ActionType.Toast,
            "Show Notification",
            "Displays a Windows system toast message",
            new() {
                new("title", "Title", ParameterInputType.Text, true, "WBSKT"),
                new("message", "Message", ParameterInputType.TextArea, true, "{{payload.text}}")
            }
        ),
        new ActionDefinition(
            ActionType.ProcessStart,
            "Launch Application",
            "Starts a specific executable or process",
            new() {
                new("path", "Executable Path", ParameterInputType.FilePath, true),
                new("args", "Arguments", ParameterInputType.Text, false, "--quiet")
            }
        ),
        new ActionDefinition(
            ActionType.ProcessKill,
            "Terminate Application",
            "Closes all instances of a specific process name",
            new() {
                new("processName", "Process Name", ParameterInputType.Text, true, "chrome")
            }
        ),
        new ActionDefinition(
            ActionType.PowerShell,
            "Run PowerShell Script",
            "Executes a local .ps1 script file",
            new() {
                new("scriptPath", "Script Path", ParameterInputType.FilePath, true)
            }
        ),
        new ActionDefinition(
            ActionType.VolumeControl,
            "Audio Control",
            "Adjusts system volume or mute state",
            new() {
                new("action", "Action", ParameterInputType.Dropdown, true, null, "set", new() { "set", "mute", "unmute", "toggle" }),
                new("level", "Level (0-100)", ParameterInputType.Number, false, "50")
            }
        ),
        new ActionDefinition(
            ActionType.SystemPower,
            "Power & Session",
            "Manages the Windows session state",
            new() {
                new("action", "Action", ParameterInputType.Dropdown, true, null, "lock", new() { "lock", "sleep", "hibernate", "restart", "shutdown" })
            }
        ),
        // new ActionDefinition(
        //     ActionType.MonitorControl,
        //     "Display Control",
        //     "Manages monitor power state",
        //     new() {
        //         new("action", "Action", ParameterInputType.Dropdown, true, null, "off", new() { "off", "on" })
        //     }
        // ),
        new ActionDefinition(
            ActionType.OpenFile,
            "Open URL or File",
            "Opens a web link or local file in the default app",
            new() {
                new("target", "URL or Path", ParameterInputType.Text, true, "https://google.com")
            }
        )
    };
}
