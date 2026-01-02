namespace Wbskt.Common.Records;

public record UserRecord
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
    public required string PasswordHash { get; init; }
}

public record UserLoginRequest
{
    public required string Email { get; set; }

    public required string Password { get; set; }
}

public record UserRegistrationRequest : UserLoginRequest
{
    public string UserName { get; set; } = string.Empty;
}
