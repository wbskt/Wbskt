using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class GuidIdGeneratorTests
{
    [Fact]
    public void NewId_ReturnsNonEmptyGuid()
    {
        // Arrange
        var generator = new GuidIdGenerator();

        // Act
        var result = generator.NewId();

        // Assert
        Assert.NotEqual(Guid.Empty, result);
    }

    [Fact]
    public void NewId_ReturnsUniqueGuids()
    {
        // Arrange
        var generator = new GuidIdGenerator();

        // Act
        var id1 = generator.NewId();
        var id2 = generator.NewId();
        var id3 = generator.NewId();

        // Assert
        Assert.NotEqual(id1, id2);
        Assert.NotEqual(id2, id3);
        Assert.NotEqual(id1, id3);
    }

    [Fact]
    public void NewId_ProducesVersion4Guids()
    {
        // Arrange
        var generator = new GuidIdGenerator();

        // Act
        var result = generator.NewId();
        var bytes = result.ToByteArray();

        // Assert - check Version 4 GUID (random)
        // Version is in byte 7, bits 4-7 should be 0100 (4)
        var versionByte = bytes[7];
        var version = (versionByte & 0xF0) >> 4;
        Assert.Equal(4, version);
    }

    [Fact]
    public void NewId_WorksAcrossMultipleCalls()
    {
        // Arrange
        var generator = new GuidIdGenerator();
        var ids = new HashSet<Guid>();

        // Act - generate 100 IDs
        for (int i = 0; i < 100; i++)
        {
            ids.Add(generator.NewId());
        }

        // Assert - all unique
        Assert.Equal(100, ids.Count);
    }
}
