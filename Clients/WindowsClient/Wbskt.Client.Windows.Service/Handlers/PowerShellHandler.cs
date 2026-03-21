using Wbskt.Client.Windows.Models;

namespace Wbskt.Client.Windows.Service.Handlers;

public class PowerShellHandler : IActionHandler
{
    public ActionType Type => ActionType.PowerShell;

    public async Task ExecuteAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("scriptPath", out var scriptPath))
        {
            return;
        }

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            CreateNoWindow = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
        };

        using var process = System.Diagnostics.Process.Start(startInfo);
        if (process != null)
        {
            await process.WaitForExitAsync();
        }
    }
}
