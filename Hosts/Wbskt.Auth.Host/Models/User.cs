namespace Wbskt.Auth.Host.Models;

public class User
{
    public int Id { get; set; }
    public Guid RefId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    /// <summary>
    /// Whether the owner of <see cref="Email"/> has proved they control it. Sign-in requires it.
    /// </summary>
    public bool IsEmailVerified { get; set; }
}
