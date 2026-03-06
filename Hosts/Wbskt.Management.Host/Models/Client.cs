using Wbskt.Common.Abstraction.Models.Management;

namespace Wbskt.Management.Host.Models;

public class Client
{
    public int Id { get; set; }
    public Guid RefId { get; set; }
    public int WorkspaceId { get; set; }
    public int PolicyId { get; set; }
    public Guid PolicyRefId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public ClientStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}