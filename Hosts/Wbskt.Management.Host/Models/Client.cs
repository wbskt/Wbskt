namespace Wbskt.Management.Host.Models;

public class Client : IWorkspaceOwned
{
    public int Id { get; set; }
    public Guid RefId { get; set; }
    public int WorkspaceId { get; set; }
    public int PolicyId { get; set; }
    public Guid PolicyRefId { get; set; }
    public string Name { get; set; } = string.Empty;
    public ClientStatus Status { get; set; }
    public bool IsConnected { get; set; }
    public DateTime? ConnectedAt { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public int? LastRttMs { get; set; }
    public DateTime? RttMeasuredAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Sorted. Filled by the list and detail reads only; other reads leave it empty.
    public IReadOnlyList<string> Tags { get; set; } = [];
}