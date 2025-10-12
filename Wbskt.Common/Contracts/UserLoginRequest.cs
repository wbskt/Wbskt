namespace Wbskt.Common.Contracts;

public record UserLoginRequest
{
    public required string EmailId { get; set; }

    public required string Password { get; set; }
}
