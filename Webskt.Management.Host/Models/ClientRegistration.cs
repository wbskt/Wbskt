namespace Webskt.Management.Host.Models;

public class Client
{
    public int Id { get; set; }
    public Guid RefId { get; set; }
    public int PolicyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public ClientStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

public record ClientRegistrationRequest(string Pin, string Name);

public record ClientRegistrationResponse(
    Guid ClientRefId, 
    string Secret, 
    ClientStatus Status
);
