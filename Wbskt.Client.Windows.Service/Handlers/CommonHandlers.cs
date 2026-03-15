using Wbskt.Client.Windows.Models;

namespace Wbskt.Client.Windows.Service.Handlers;

public class ProcessStartHandler : IActionHandler
{
    public ActionType Type => ActionType.ProcessStart;

    public Task ExecuteAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("path", out var path))
        {
            return Task.CompletedTask;
        }

        var args = parameters.GetValueOrDefault("args", "");
        
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path, args) 
        { 
            UseShellExecute = true 
        });
        
        return Task.CompletedTask;
    }
}

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
