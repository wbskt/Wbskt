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

public record ClientCapabilities(
    string Agent,
    string Version,
    string OS,
    List<CommandCapability> Capabilities
);
