namespace Wbskt.Core.Service.Contracts;

public record CreatePolicyRequest
{
    public required string Name { get; init; }
    public int? MaxClients { get; init; }
    public DateTime? Expiry { get; init; }
    public int Pin { get; init; }
}
