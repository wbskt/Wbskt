namespace Wbskt.Core.Service.Contracts;

public record ClientRegistrationResponse
{
    public required string AuthToken { get; init; }
    public required string SocketServerAddress { get; init; }
}
