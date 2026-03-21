using System.Diagnostics;
using Wbskt.Client.Windows.Models;

namespace Wbskt.Client.Windows.Service.Handlers;

public class OpenFileHandler : IActionHandler
{
    public ActionType Type => ActionType.OpenFile;

    public Task ExecuteAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("target", out var target))
        {
            return Task.CompletedTask;
        }

        Process.Start(new ProcessStartInfo(target) 
        { 
            UseShellExecute = true 
        });

        return Task.CompletedTask;
    }
}
