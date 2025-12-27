namespace Wbskt.Management.Api.Contracts;

public record UpdatePolicyRequest
{
    public required string Name { get; init; }
    public int? MaxClients { get; init; }
    public DateTime? Expiry { get; init; }
}
