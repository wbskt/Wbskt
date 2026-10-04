namespace Wbskt.Auth.Host.Models;

public class RefreshToken
{
    public int Id { get; set; }
    public int UserId { get; set; }

    /// <summary>The sign-in this token belongs to, unchanged across rotations. The access token's <c>sid</c>.</summary>
    public Guid SessionId { get; set; }

    /// <summary>
    /// The token itself, set only on one this host has just minted and is about to hand out. The
    /// database stores its hash, so a token read back from it leaves this empty.
    /// </summary>
    public string Token { get; set; } = string.Empty;
    public DateTime Expires { get; set; }
    public DateTime? Revoked { get; set; }
    public bool IsActive => Revoked == null && DateTime.UtcNow < Expires;
}
