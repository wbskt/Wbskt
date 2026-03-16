using Wbskt.Client.Windows.Models;

namespace Wbskt.Client.Windows.Service.Handlers;

public class ToastHandler : IActionHandler
{
    public ActionType Type => ActionType.Toast;

    public async Task ExecuteAsync(Dictionary<string, string> parameters)
    {
        var title = parameters.GetValueOrDefault("title", "WBSKT");
        var message = parameters.GetValueOrDefault("message", "");

        // Using PowerShell to trigger a Windows Toast without adding heavy NuGet dependencies for now.
        var script = $"[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null; " +
                     $"[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null; " +
                     $"$template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02); " +
                     $"$toastXml = [Windows.Data.Xml.Dom.XmlDocument]::new(); " +
                     $"$toastXml.LoadXml($template.GetXml()); " +
                     $"$toastXml.GetElementsByTagName('text').Item(0).AppendChild($toastXml.CreateTextNode('{title}')) | Out-Null; " +
                     $"$toastXml.GetElementsByTagName('text').Item(1).AppendChild($toastXml.CreateTextNode('{message}')) | Out-Null; " +
                     $"$toast = [Windows.UI.Notifications.ToastNotification]::new($toastXml); " +
                     $"[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('WBSKT').Show($toast);";

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
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
