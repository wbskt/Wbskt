namespace Wbskt.Auth.Host.Controllers;

/// <summary>
/// Marks an action that hashes or verifies a password. Those share one concurrency limit across the
/// whole host (see <c>Program</c>), on top of their per-IP rate limit.
/// </summary>
/// <remarks>
/// A password hash is deliberately expensive: PBKDF2 at around 100k iterations holds a core for tens
/// of milliseconds. The per-IP limit stops one source, but a flood from many addresses could still
/// pin every core of the VM and slow every host running on it. Capping how many hashes run at once
/// keeps the rest of the machine responsive; past the cap, requests wait in a short queue and then
/// get a 429.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class HashesPasswordAttribute : Attribute;
