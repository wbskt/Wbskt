namespace Wbskt.Core.Service.Contracts;

public record PolicyResponse
{
    public int Id { get; init; }
    public Guid RefId { get; init; }
    public required string Name { get; init; }
    public int? MaxClients { get; init; }
    public DateTime? Expiry { get; init; }
    public int Pin { get; init; }
}
