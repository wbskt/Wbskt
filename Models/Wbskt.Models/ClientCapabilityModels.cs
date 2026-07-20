namespace Wbskt.Models;

// Host-side mirror of the SDK wire contract (Wbskt.Client.Sdk.Models.CapabilitiesModels).
// The SDK stays dependency-free, so the shape is duplicated; property names must match
// because the "capabilities" socket message is deserialized into these records.

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
