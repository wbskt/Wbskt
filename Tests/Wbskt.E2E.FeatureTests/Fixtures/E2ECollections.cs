namespace Wbskt.E2E.FeatureTests.Fixtures;

// The suite runs as three collections, one per area, and xunit.runner.json lets the three run at the
// same time. Inside a collection the classes still run one after another, as the whole suite did
// before. Running side by side is safe because every scenario arranges a user and workspace of its
// own (CreateIsolatedWorkspaceAsync), so nothing one collection creates shows up in another's lists.
//
// Each collection gets its own ServicesFixture. The fixture holds an HttpClient and the result of its
// host probes and nothing else, so three of them are no different from one.
//
// Some scenarios must still run alone, and are opt-in so the parallel pass never runs them:
// RateLimitingTests (auth) spends the per-IP limiter budget every collection shares, and
// DeviceRevocationTests.DEV_REV_02 (devices) restarts the socket host, dropping every live device
// connection. CI runs each in a pass of its own with --filter; see the README. The RateLimited429 row of
// ErrorShapeContractTests (engine) spends the same per-IP budget, so it is gated and run the same way,
// as are the Scenarios/Outage classes (engine), which stop the broker, Redis and the leading engine.

/// <summary>Sign-in, tokens, tenants and workspaces: <c>Scenarios/Auth</c>.</summary>
[CollectionDefinition(Name)]
public sealed class AuthCollection : ICollectionFixture<ServicesFixture>
{
    public const string Name = "E2E auth";
}

/// <summary>Device registration, sign-in, commands and revocation: <c>Scenarios/Devices</c>.</summary>
[CollectionDefinition(Name)]
public sealed class DevicesCollection : ICollectionFixture<ServicesFixture>
{
    public const string Name = "E2E devices";
}

/// <summary>Workflows, triggers and runs: every other folder under <c>Scenarios</c>.</summary>
[CollectionDefinition(Name)]
public sealed class EngineCollection : ICollectionFixture<ServicesFixture>
{
    public const string Name = "E2E engine";
}
