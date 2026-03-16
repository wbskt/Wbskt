using System.Diagnostics;
using Wbskt.Client.Windows.Models;

namespace Wbskt.Client.Windows.Service.Handlers;

public class ProcessKillHandler : IActionHandler
{
    public ActionType Type => ActionType.ProcessKill;

    public Task ExecuteAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("processName", out var processName))
        {
            return Task.CompletedTask;
        }

        var name = processName.EndsWith(".exe") ? processName[..^4] : processName;
        
        var processes = Process.GetProcessesByName(name);
        foreach (var p in processes)
        {
            try
            {
                p.Kill();
            }
            catch
            {
                // Ignore errors for individual processes
            }
        }

        return Task.CompletedTask;
    }
}
