using Webskt.Common.Abstraction.Models.Management;

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

public class Client
{
    public int Id { get; set; }
    public Guid RefId { get; set; }
    public int PolicyId { get; set; }
    public Guid PolicyRefId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public ClientStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}
