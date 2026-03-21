namespace Wbskt.Auth.Host.Models;

public record LoginRequest(string Email, string Password);
public record LoginResponse(string AccessToken, string RefreshToken);
