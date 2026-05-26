using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests;

/// <summary>
/// Shared xunit collection: all test classes decorated with [Collection("SqlEdge")] share
/// one <see cref="SqlEdgeFixture"/> instance, so the DACPAC deploy runs only once.
/// </summary>
[CollectionDefinition("SqlEdge")]
public sealed class SqlEdgeCollection : ICollectionFixture<SqlEdgeFixture>;
