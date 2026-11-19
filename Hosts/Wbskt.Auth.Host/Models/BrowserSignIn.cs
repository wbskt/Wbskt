using Wbskt.Auth.Models;

namespace Wbskt.Auth.Host.Models;

/// <summary>A browser's sign-in: the session, and the device token its cookie now carries and when it lapses.</summary>
public sealed record BrowserSignIn(LoginResponse Session, string DeviceToken, DateTimeOffset DeviceExpiresAt);
