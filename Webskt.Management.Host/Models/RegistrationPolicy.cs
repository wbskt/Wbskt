namespace Webskt.Management.Host.Models;

public class RegistrationPolicy
{
    public int Id { get; set; }
    public Guid RefId { get; set; }
    public string Pin { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? MaxClients { get; set; }
    public bool AutoApproval { get; set; }
    public DateTime CreatedAt { get; set; }
}

public record RegistrationPolicyRequest(
    string Name, 
    int? MaxClients, 
    bool AutoApproval = true
);

public record RegistrationPolicyResponse(
    Guid RefId, 
    string Pin, 
    string Name, 
    int? MaxClients, 
    bool AutoApproval,
    DateTime CreatedAt
);
