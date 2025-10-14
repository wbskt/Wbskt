namespace Wbskt.Identity.Api.Contracts;

public record ClientRegistrationResponse
{
    public required string AuthToken { get; init; }
    public required string SocketServerAddress { get; init; }
}
