using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;
using Wbskt.Protocol;

namespace Wbskt.Events.Client;

/// <summary>
/// A device's capabilities, as stored: the whole report, so a listener can patch its copy instead of
/// reading the client again. Raised only when the report was newer than the one stored.
/// </summary>
/// <param name="System">The platform features it takes (<c>["ota"]</c>); empty when none.</param>
/// <param name="Commands">The commands it declared.</param>
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientCapabilitiesUpdatedEvent")]
public sealed record ClientCapabilitiesUpdatedEvent(
    Guid ClientRefId,
    int ClientId,
    int WorkspaceId,
    string AgentName,
    string AgentVersion,
    string Platform,
    string? FirmwareVersion = null,
    IReadOnlyList<string>? System = null,
    IReadOnlyList<CommandCapability>? Commands = null
) : BaseEvent, IClientContext;
