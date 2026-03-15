using System.Runtime.InteropServices;
using Wbskt.Client.Windows.Models;

namespace Wbskt.Client.Windows.Service.Handlers;

public partial class SystemControlHandler : IActionHandler
{
    public ActionType Type => ActionType.SystemControl;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LockWorkStation();

    [LibraryImport("user32.dll")]
    public static partial void SendMessageW(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MONITORPOWER = 0xF170;

    public Task ExecuteAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("action", out var action))
        {
            return Task.CompletedTask;
        }

        switch (action.ToLower())
        {
            case "lock":
                LockWorkStation();
                break;
            case "monitoroff":
                // Handle is typically (IntPtr)0xffff for all windows (broadcast)
                // But better to use a specific window or a reliable method
                SendMessageW((IntPtr)0xffff, WM_SYSCOMMAND, (IntPtr)SC_MONITORPOWER, (IntPtr)2);
                break;
        }

        return Task.CompletedTask;
    }
}
