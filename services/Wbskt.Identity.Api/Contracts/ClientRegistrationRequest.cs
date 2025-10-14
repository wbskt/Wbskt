namespace Wbskt.Identity.Api.Contracts;

public record ClientRegistrationRequest
{
    public required string Pin { get; init; }
    public string? ClientName { get; init; }
}
