using System.Runtime.InteropServices;
using Wbskt.Client.Windows.Models;

namespace Wbskt.Client.Windows.Service.Handlers;

public class VolumeControlHandler : IActionHandler
{
    public ActionType Type => ActionType.VolumeControl;

    public async Task ExecuteAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("action", out var action))
        {
            return;
        }

        await Task.Run(() =>
        {
            IAudioEndpointVolume? device = null;
            try
            {
                device = GetDefaultRenderDevice();
                if (device == null)
                {
                    return;
                }

                switch (action.ToLowerInvariant())
                {
                    case "mute":
                        Marshal.ThrowExceptionForHR(device.SetMute(true));
                        break;

                    case "unmute":
                        Marshal.ThrowExceptionForHR(device.SetMute(false));
                        break;

                    case "toggle":
                        Marshal.ThrowExceptionForHR(device.GetMute(out bool isMuted));
                        Marshal.ThrowExceptionForHR(device.SetMute(!isMuted));
                        break;

                    case "set" when parameters.TryGetValue("level", out var levelStr):
                        if (float.TryParse(levelStr, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var level))
                        {
                            // "level" is expected as 0–100; clamp to valid scalar range
                            float scalar = Math.Clamp(level / 100f, 0f, 1f);
                            Marshal.ThrowExceptionForHR(device.SetMasterVolumeLevelScalar(scalar));
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                // TODO: inject a proper ILogger and log ex here
                System.Diagnostics.Debug.WriteLine($"[VolumeControlHandler] {ex}");
            }
            finally
            {
                if (device != null)
                {
                    Marshal.ReleaseComObject(device);
                }
            }
        });
    }

    private static IAudioEndpointVolume? GetDefaultRenderDevice()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
            Marshal.ThrowExceptionForHR(
                enumerator.GetDefaultAudioEndpoint(
                    EDataFlow.eRender, ERole.eMultimedia, out device));

            var iid = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(
                device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var obj));

            return (IAudioEndpointVolume)obj;
        }
        finally
        {
            if (device   != null)
            {
                Marshal.ReleaseComObject(device);
            }

            if (enumerator != null)
            {
                Marshal.ReleaseComObject(enumerator);
            }
        }
    }

    private const int CLSCTX_ALL = 0x17; // = 23, named for clarity
}

#region COM enums

internal enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
internal enum ERole    { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

#endregion

#region COM interfaces & coclass

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
[ClassInterface(ClassInterfaceType.None)]
internal class MMDeviceEnumeratorClass { }

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
internal interface IMMDeviceEnumerator
{
    // slot 3 – EnumAudioEndpoints (skipped)
    [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int dwStateMask, out object ppDevices);

    // slot 4 – GetDefaultAudioEndpoint
    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);

    // remaining slots not needed; vtable layout preserved by ordering
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("D666063F-1587-4E2B-9013-A6D143C72F92")]  // corrected GUID for IMMDevice
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams,
                 [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
}

/// <summary>
/// IAudioEndpointVolume — vtable layout (indices are 0-based, IUnknown = 0-2):
///  3  RegisterControlChangeNotify
///  4  UnregisterControlChangeNotify
///  5  GetChannelCount
///  6  SetMasterVolumeLevel        (absolute dB)
///  7  GetMasterVolumeLevel        (absolute dB)
///  8  SetMasterVolumeLevelScalar  ← we need this
///  9  GetMasterVolumeLevelScalar
/// 10  SetChannelVolumeLevel
/// 11  GetChannelVolumeLevel
/// 12  SetChannelVolumeLevelScalar
/// 13  GetChannelVolumeLevelScalar
/// 14  SetMute                     ← and this
/// 15  GetMute                     ← and this
/// 16  GetVolumeStepInfo
/// 17  VolumeStepUp
/// 18  VolumeStepDown
/// 19  QueryHardwareSupport
/// 20  GetVolumeRange
/// </summary>
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
internal interface IAudioEndpointVolume
{
    // slots 3–4: notification registration (unused)
    [PreserveSig] int RegisterControlChangeNotify(IntPtr pNotify);
    [PreserveSig] int UnregisterControlChangeNotify(IntPtr pNotify);

    // slot 5
    [PreserveSig] int GetChannelCount(out uint pnChannelCount);

    // slots 6–7: absolute dB level (unused)
    [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, [Optional] ref Guid pguidEventContext);
    [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);

    // slot 8  ← the one we call
    [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel,
        [Optional] ref Guid pguidEventContext);

    // slot 9
    [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);

    // slots 10–13: per-channel (unused)
    [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, [Optional] ref Guid pguidEventContext);
    [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, [Optional] ref Guid pguidEventContext);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);

    // slots 14–15  ← the ones we call
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute,
        [Optional] ref Guid pguidEventContext);

    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
}

#endregion