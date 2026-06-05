namespace Wbskt.E2E.FeatureTests.Fixtures;

[CollectionDefinition(E2ECollection.Name)]
public sealed class E2ECollection : ICollectionFixture<ServicesFixture>
{
    public const string Name = "E2E";
}
