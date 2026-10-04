namespace Wbskt.Management.Host.Models;

// Client row joined with its policy name and capability sidecar (Client_GetDetailBy_RefId).
// Capability fields are null until the client first reports capabilities.
public class ClientDetail : Client
{
    public string PolicyName { get; set; } = string.Empty;

    // Socket-host instance holding the live connection; null while offline. Commands are
    // addressed to it so that exactly one host answers when the connection is gone.
    public string? ConnectedHostId { get; set; }
    public string? AgentName { get; set; }
    public string? AgentVersion { get; set; }
    public string? Platform { get; set; }
    public string? CapabilitiesJson { get; set; }
    public DateTime? CapabilitiesUpdatedAt { get; set; }
}


// Where a command goes: the client's internal id and the socket-host instance holding its
// connection (Clients.ConnectedHostId).
public sealed record ClientCommandTarget(int ClientId, string HostId);
