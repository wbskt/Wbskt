using System.Reflection;
using System.Runtime.InteropServices;

namespace Wbskt.Client.Sdk.Internal;

// Auto-detected metadata reported to the platform in the "capabilities" message,
// so every device shows its SDK and platform without any app code.
internal static class SdkInfo
{
    public const string AgentName = "csharp-sdk";

    public static readonly string Version = ResolveVersion();

    public static readonly string Platform = RuntimeInformation.RuntimeIdentifier;

    private static string ResolveVersion()
    {
        var informational = typeof(SdkInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip any "+<commit>" source-link suffix.
            var plusIndex = informational.IndexOf('+');
            return plusIndex > 0 ? informational[..plusIndex] : informational;
        }

        return typeof(SdkInfo).Assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}
