using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Telemetry;

namespace Wbskt.Auth.Host.Services.Passwords;

/// <summary>
/// Every password hash and verification the host makes, behind one host-wide limit on how many run at
/// once. A hash is deliberately expensive (PBKDF2 holds a core for tens of milliseconds), so a flood
/// from many addresses, which the per-IP limiter cannot see, could otherwise pin every core of the VM.
/// </summary>
/// <remarks>
/// The limit covers the hash alone. It used to cover the whole request, so a slow breach check or
/// database held the slots that sign-in needed for hashing.
/// </remarks>
internal sealed class PasswordHashing : IDisposable
{
    private readonly IPasswordHasher<User> _hasher;
    private readonly AuthMetrics _metrics;
    private readonly SemaphoreSlim _slots;
    private readonly Lazy<string> _dummyHash;

    public PasswordHashing(IPasswordHasher<User> hasher, AuthMetrics metrics, int concurrency)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(concurrency, 1);
        _hasher = hasher;
        _metrics = metrics;
        _slots = new SemaphoreSlim(concurrency, concurrency);

        // A hash of a password nobody knows, checked when there is no real one, so a sign-in for an
        // address with no account (or a locked one) costs the same work as one with a wrong password.
        _dummyHash = new Lazy<string>(() => _hasher.HashPassword(new User(), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));
    }

    /// <summary>Twice the cores keeps them busy without starving the rest of the host.</summary>
    public static int DefaultConcurrency => Environment.ProcessorCount * 2;

    public async Task<string> HashAsync(string password, CancellationToken cancellationToken)
    {
        // PasswordHasher<T> ignores the user when hashing, so a bare one is right even with no account.
        return await RunAsync(() => _hasher.HashPassword(new User(), password), cancellationToken);
    }

    public async Task<PasswordVerificationResult> VerifyAsync(User user, string password, CancellationToken cancellationToken)
    {
        return await RunAsync(() => _hasher.VerifyHashedPassword(user, user.PasswordHash, password), cancellationToken);
    }

    /// <summary>
    /// Spends the work of one verification without testing anything, for the paths that have no real
    /// hash to check, so they take as long as the ones that do.
    /// </summary>
    public async Task SpendAsync(string password, CancellationToken cancellationToken)
    {
        await RunAsync(() => _hasher.VerifyHashedPassword(new User(), _dummyHash.Value, password), cancellationToken);
    }

    public void Dispose() => _slots.Dispose();

    private async Task<T> RunAsync<T>(Func<T> hash, CancellationToken cancellationToken)
    {
        await _slots.WaitAsync(cancellationToken);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = hash();
            _metrics.RecordPasswordHash(stopwatch.Elapsed.TotalMilliseconds);
            return result;
        }
        finally
        {
            _slots.Release();
        }
    }
}
