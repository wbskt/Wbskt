namespace Wbskt.Management.Api.Contracts;

public record RefreshTokenRequest
{
    public required string RefreshToken { get; init; }
}
