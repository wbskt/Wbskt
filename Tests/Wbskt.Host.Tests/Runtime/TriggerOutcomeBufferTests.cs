using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Host.Tests.Runtime;

public sealed class TriggerOutcomeBufferTests
{
    private static TriggerDispatchOutcomeRow Row(Guid node, int n) => new()
    {
        WorkflowRefId = Guid.Empty,
        TriggerNodeId = node,
        OccurredAt = DateTime.UnixEpoch.AddSeconds(n),
        ChannelKind = "client",
        MessageType = $"m{n}",
        Outcome = "Filtered"
    };

    [Fact]
    public void Only_the_newest_outcomes_of_each_trigger_wait_and_a_drain_empties_it()
    {
        var buffer = new TriggerOutcomeBuffer();
        Guid chatty = Guid.NewGuid();
        Guid quiet = Guid.NewGuid();

        for (int n = 0; n < 1000; n++)
        {
            buffer.Add(Row(chatty, n));
        }
        buffer.Add(Row(quiet, 0));

        var drained = buffer.Drain();

        var chattyRows = drained.Where(r => r.TriggerNodeId == chatty).Select(r => r.MessageType).ToList();
        Assert.Equal(Enumerable.Range(1000 - ITriggerDispatchOutcomeProvider.Keep, ITriggerDispatchOutcomeProvider.Keep).Select(n => $"m{n}"), chattyRows);
        Assert.Single(drained, r => r.TriggerNodeId == quiet);
        Assert.Empty(buffer.Drain());
    }
}
