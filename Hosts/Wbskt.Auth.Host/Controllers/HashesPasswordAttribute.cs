namespace Wbskt.Auth.Host.Controllers;

/// <summary>
/// Marks an action that hashes or verifies a password. Those share one coarse concurrency limit across
/// the whole host (see <c>Program</c>), on top of their per-IP rate limit, which bounds how many wait
/// for <see cref="Services.Passwords.PasswordHashing"/>; past it, requests wait in a short queue and
/// then get a 429. The hashes themselves are limited by PasswordHashing.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class HashesPasswordAttribute : Attribute;
