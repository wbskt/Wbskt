using FluentAssertions;
using Wbskt.Workflow.Engine.Host.Tests.Infrastructure;
using Wbskt.Workflow.Providers;

namespace Tests.Wbskt.Workflow.Engine.Host.Tests.Providers;

public class JoinAggregatorProviderTests
{
    private static readonly Guid JoinNodeId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [Fact]
    public void Map_reads_ShouldContinue_true_and_all_counts()
    {
        var reader = FakeSqlDataReader.From(new[]
        {
            new Dictionary<string, object?>
            {
                ["ShouldContinue"]  = true,
                ["ContributedCount"] = 3,
                ["SucceededCount"]   = 3,
                ["FailedCount"]      = 0,
                ["ExpectedCount"]    = 3,
                ["JoinNodeId"]       = JoinNodeId
            }
        });

        reader.Read();
        var result = JoinAggregatorProvider.Map(reader);

        result.ShouldContinue.Should().BeTrue();
        result.ContributedCount.Should().Be(3);
        result.SucceededCount.Should().Be(3);
        result.FailedCount.Should().Be(0);
        result.ExpectedCount.Should().Be(3);
        // BranchLoop needs this to spawn the continuation branch when a failed arrival meets quorum.
        result.JoinNodeId.Should().Be(JoinNodeId);
    }

    [Fact]
    public void Map_reads_ShouldContinue_false_when_quorum_not_claimed()
    {
        var reader = FakeSqlDataReader.From(new[]
        {
            new Dictionary<string, object?>
            {
                ["ShouldContinue"]  = false,
                ["ContributedCount"] = 1,
                ["SucceededCount"]   = 1,
                ["FailedCount"]      = 0,
                ["ExpectedCount"]    = 3,
                // A Fork cohort with no Join downstream stores a null JoinNodeId - Map must tolerate it.
                ["JoinNodeId"]       = null
            }
        });

        reader.Read();
        var result = JoinAggregatorProvider.Map(reader);

        result.ShouldContinue.Should().BeFalse();
        result.ContributedCount.Should().Be(1);
        result.SucceededCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        result.ExpectedCount.Should().Be(3);
        result.JoinNodeId.Should().BeNull();
    }

    [Fact]
    public void Map_reads_failed_outcome_counts()
    {
        var reader = FakeSqlDataReader.From(new[]
        {
            new Dictionary<string, object?>
            {
                ["ShouldContinue"]  = false,
                ["ContributedCount"] = 2,
                ["SucceededCount"]   = 1,
                ["FailedCount"]      = 1,
                ["ExpectedCount"]    = 5,
                ["JoinNodeId"]       = JoinNodeId
            }
        });

        reader.Read();
        var result = JoinAggregatorProvider.Map(reader);

        result.ShouldContinue.Should().BeFalse();
        result.ContributedCount.Should().Be(2);
        result.SucceededCount.Should().Be(1);
        result.FailedCount.Should().Be(1);
        result.ExpectedCount.Should().Be(5);
    }
}
