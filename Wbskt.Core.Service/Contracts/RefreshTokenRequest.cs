namespace Wbskt.Core.Service.Contracts;

public record RefreshTokenRequest
{
    public required string RefreshToken { get; init; }
}
