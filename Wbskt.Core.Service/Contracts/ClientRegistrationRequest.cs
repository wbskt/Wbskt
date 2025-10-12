namespace Wbskt.Core.Service.Contracts;

public record ClientRegistrationRequest
{
    public Guid PolicyRefId { get; init; }
    public int Pin { get; init; }
    public string? ClientName { get; init; }
}
