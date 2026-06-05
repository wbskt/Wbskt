using Wbskt.Client.Sdk;

namespace Wbskt.E2E.FeatureTests.Fixtures;

/// <summary>
/// Simple in-memory credential store for WbsktClient — avoids file I/O in tests
/// and allows pre-seeding credentials obtained via the helper API.
/// </summary>
public sealed class InMemoryClientStorage : IClientStorage
{
    private Guid? _refId;
    private string? _secret;

    public InMemoryClientStorage() { }

    public InMemoryClientStorage(Guid refId, string secret)
    {
        _refId = refId;
        _secret = secret;
    }

    public Task SaveCredentialsAsync(Guid refId, string secret)
    {
        _refId = refId;
        _secret = secret;
        return Task.CompletedTask;
    }

    public Task<(Guid? RefId, string? Secret)> LoadCredentialsAsync() =>
        Task.FromResult((_refId, _secret));

    public Task ClearCredentialsAsync()
    {
        _refId = null;
        _secret = null;
        return Task.CompletedTask;
    }
}
