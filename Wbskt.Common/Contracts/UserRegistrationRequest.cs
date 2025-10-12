namespace Wbskt.Common.Contracts;

public record UserRegistrationRequest : UserLoginRequest
{
    public string UserName { get; set; } = string.Empty;
}
