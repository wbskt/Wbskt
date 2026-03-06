namespace Wbskt.Management.Host.Models;

public class RegistrationPolicy
{
    public int Id { get; set; }
    public Guid RefId { get; set; }
    public int WorkspaceId { get; set; }
    public string Pin { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? MaxClients { get; set; }
    public bool AutoApproval { get; set; }
    public DateTime CreatedAt { get; set; }
}