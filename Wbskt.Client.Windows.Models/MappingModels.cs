namespace Wbskt.Client.Windows.Models;

public enum ActionType
{
    Toast,          // Windows Notifications
    ProcessStart,   // Launching .exe
    PowerShell,     // Running scripts
    SystemControl,  // Volume, Lock, Sleep
    KeySimulation   // Virtual Keypresses
}

public record CommandMapping(
    Guid Id,
    string CommandName, 
    ActionType ActionType, 
    Dictionary<string, string> Parameters,
    bool IsEnabled = true
);

public interface IActionHandler
{
    ActionType Type { get; }
    Task ExecuteAsync(Dictionary<string, string> resolvedParameters);
}
