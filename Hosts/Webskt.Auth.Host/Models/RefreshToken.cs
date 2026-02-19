namespace Webskt.Auth.Host.Models;

public class RefreshToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime Expires { get; set; }
    public DateTime? Revoked { get; set; }
    public string? ReplacedByToken { get; set; }
    public bool IsActive => Revoked == null && DateTime.UtcNow < Expires;
}
