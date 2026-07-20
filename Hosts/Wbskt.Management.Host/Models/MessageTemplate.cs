namespace Wbskt.Management.Host.Models;

public class MessageTemplate
{
    public int Id { get; set; }
    public Guid RefId { get; set; }
    public int WorkspaceId { get; set; }
    public int? PolicyId { get; set; }
    public Guid? PolicyRefId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
