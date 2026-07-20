namespace Wbskt.Management.Host.Services;

// Message types owned by the platform protocol. Devices and users must not send them as
// ordinary payloads: sys.* frames never reach the generic pipeline, and "capabilities" /
// "state.report" are destructured by ClientMetadataIngestionHandler.
public static class ReservedMessageTypes
{
    public const string Capabilities = "capabilities";
    public const string StateReport = "state.report";
    public const string SystemPrefix = "sys.";

    public static bool IsReserved(string messageType)
    {
        return messageType.StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase)
               || messageType.Equals(Capabilities, StringComparison.OrdinalIgnoreCase)
               || messageType.Equals(StateReport, StringComparison.OrdinalIgnoreCase);
    }
}
