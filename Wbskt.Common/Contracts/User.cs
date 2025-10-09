namespace Wbskt.Common.Contracts;

public class User
{
    public int UserId { get; set; }

    public required string Name { get; set; }

    public required string EmailId { get; set; }

    public required string PasswordHash { get; set; }
}
