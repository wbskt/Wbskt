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