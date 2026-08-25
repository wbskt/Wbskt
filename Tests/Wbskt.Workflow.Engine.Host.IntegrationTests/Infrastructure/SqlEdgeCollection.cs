using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests;

/// <summary>
/// Shared xunit collection: all test classes decorated with [Collection("SqlEdge")] share one
/// <see cref="SqlEdgeFixture"/> and one <see cref="AuthSqlFixture"/>, so each DACPAC is deployed only
/// once. A class takes whichever of the two it needs as a constructor parameter.
/// </summary>
/// <remarks>
/// <b>One collection, deliberately, even though these are two separate databases.</b> xunit runs
/// distinct collections in parallel, and every database test in this project shares a single SQL
/// Server instance. Putting the auth fixture in a collection of its own is what a second database
/// seems to call for, and it briefly was — the result was an unrelated failure in
/// <c>RunGetStuckIntegrationTests</c>, which had passed on the commit before. The suite was written
/// against a serial runner and is not audited for concurrent execution; making it parallel is a
/// change worth making deliberately, with the tests reviewed for it, rather than as a side effect of
/// adding a fixture.
/// </remarks>
[CollectionDefinition("SqlEdge")]
public sealed class SqlEdgeCollection : ICollectionFixture<SqlEdgeFixture>, ICollectionFixture<AuthSqlFixture>;
