namespace Wbskt.Protocol;

// The payload of the "capabilities" message (MessageTypes.Capabilities). The SDK sends these and the
// management host reads them, so property names are the contract.

public record PropertySchema(
    string Name,
    string Label,
    string DataType, // "string", "number", "boolean", "object"
    string Description,
    bool IsRequired = true,
    string? DefaultValue = null
);

public record CommandCapability(
    string Command,
    string Description,
    List<PropertySchema> Parameters
);

/// <param name="System">The platform features the device takes, such as <see cref="SystemCapabilities.Ota"/>. Absent means none.</param>
public record ClientCapabilities(
    string Agent,
    string Version,
    string OS,
    List<CommandCapability> Capabilities,
    List<string>? System = null
);

/// <summary>The names a device may list under <c>system</c> in its capabilities.</summary>
public static class SystemCapabilities
{
    /// <summary>Takes <see cref="MessageTypes.OtaUpdate"/>.</summary>
    public const string Ota = "ota";
}
