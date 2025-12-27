namespace Wbskt.Management.Api.Contracts;

public record UserLoginResponse
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
}
