namespace Wbskt.Client.Windows.Models;

public enum ActionType
{
    Toast,          // Windows Notifications
    ProcessStart,   // Launching .exe
    PowerShell,     // Running scripts
    SystemControl,  // Volume, Lock, Sleep
    KeySimulation   // Virtual Keypresses
}

public record CommandMapping(Guid Id)
{
    public string CommandName { get; set; } = string.Empty;
    public ActionType ActionType { get; set; } = ActionType.Toast;
    public Dictionary<string, string> Parameters { get; set; } = new();
    public bool IsEnabled { get; set; } = true;
}

public interface IActionHandler
{
    ActionType Type { get; }
    Task ExecuteAsync(Dictionary<string, string> resolvedParameters);
}
