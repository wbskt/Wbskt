using System.Runtime.InteropServices;
using Wbskt.Client.Windows.Models;

namespace Wbskt.Client.Windows.Service.Handlers;

public partial class SystemPowerHandler : IActionHandler
{
    public ActionType Type => ActionType.SystemPower;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LockWorkStation();

    [LibraryImport("user32.dll", EntryPoint = "ExitWindowsEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ExitWindowsEx(uint uFlags, uint dwReason);

    [LibraryImport("PowrProf.dll", EntryPoint = "SetSuspendState")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetSuspendState(byte hibernate, [MarshalAs(UnmanagedType.Bool)] bool forceCritical, [MarshalAs(UnmanagedType.Bool)] bool disableWakeEvent);

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
            case "sleep":
                SetSuspendState(0, false, false);
                break;
            case "hibernate":
                SetSuspendState(1, false, false);
                break;
            case "restart":
                System.Diagnostics.Process.Start("shutdown", "/r /t 0");
                break;
            case "shutdown":
                System.Diagnostics.Process.Start("shutdown", "/s /t 0");
                break;
        }

        return Task.CompletedTask;
    }
}
