namespace Wbskt.Management.Api.Contracts;

public record SaveCredentialsRequest
{
    public required string IntegrationType { get; init; }
    public required string Name { get; init; }
    public required string Credentials { get; init; }
}
