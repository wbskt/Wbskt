namespace Wbskt.Auth.Host.Models;

public class Tenant
{
    public int Id { get; set; }
    public Guid RefId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}
