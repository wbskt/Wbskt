namespace Wbskt.Management.Host.Models;

// Client row joined with its policy name and capability sidecar (Client_GetDetailBy_RefId).
// Capability fields are null until the client first reports capabilities.
public class ClientDetail : Client
{
    public string PolicyName { get; set; } = string.Empty;
    public string? AgentName { get; set; }
    public string? AgentVersion { get; set; }
    public string? Platform { get; set; }
    public string? CapabilitiesJson { get; set; }
    public DateTime? CapabilitiesUpdatedAt { get; set; }
}
