namespace Wbskt.Identity.Api.Contracts;

public record PolicyResponse
{
    public Guid RefId { get; init; }
    public required string Name { get; init; }
    public int? MaxClients { get; init; }
    public DateTime? Expiry { get; init; }
    public string Pin { get; init; }
}
