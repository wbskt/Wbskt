using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Channels;
using Wbskt.Workflow.Runtime.BranchResults;
using static Wbskt.Host.Tests.Runtime.BranchResults.BranchResultTestData;

namespace Wbskt.Host.Tests.Runtime.BranchResults;

public sealed class WaitForBookmarkResultHandlerTests
{
    private static readonly Guid WaitNode = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly List<string> _log = [];
    private readonly List<BookmarkRow> _bookmarks = [];
    private readonly RecordingBranchProvider _branches;
    private readonly RecordingRunJournal _journal;
    private readonly WaitForBookmarkResultHandler _handler;

    public WaitForBookmarkResultHandlerTests()
    {
        _branches = new RecordingBranchProvider(_log, Branch(WaitNode, """{"keep":1}"""));
        _journal = new RecordingRunJournal(_log);

        var bookmarkProvider = new Mock<IBookmarkProvider>();
        bookmarkProvider
            .Setup(provider => provider.CreateAsync(It.IsAny<BookmarkRow>(), It.IsAny<CancellationToken>()))
            .Returns((BookmarkRow row, CancellationToken _) =>
            {
                _log.Add("bookmark");
                _bookmarks.Add(row);
                return Task.FromResult(row);
            });

        _handler = new WaitForBookmarkResultHandler(_branches, bookmarkProvider.Object, new SequentialIdGenerator(), new FixedClock(), _journal);
    }

    [Fact]
    public async Task Parks_the_branch_then_writes_its_one_bookmark_then_runs_the_follow_up()
    {
        var condition = new SignalWakeCondition("approve", "scope-1") { Ttl = TimeSpan.FromMinutes(5), TtlPort = "timeout" };
        var wait = new NodeExecutionResult.WaitForBookmark(condition, Patch("waiting", true))
        {
            AfterParked = _ =>
            {
                _log.Add("after-parked");
                return Task.CompletedTask;
            }
        };

        BranchResultOutcome outcome = await _handler.HandleAsync(wait, Context(Node(WaitNode), Definition([Node(WaitNode)], []), _branches.Current), CancellationToken.None);

        Assert.True(outcome.IsStopped);
        Assert.Equal(
            new[] { $"update:{BranchStatus.Waiting}:{WaitNode}", $"journal:{HistoryEventKind.BranchParked}", "bookmark", "after-parked" },
            _log);

        var localState = LocalState(Assert.Single(_branches.Updates).LocalJson);
        Assert.Equal(1, localState["keep"].GetInt32());
        Assert.True(localState["waiting"].GetBoolean());

        RunJournalEntry parked = Assert.Single(_journal.Entries);
        Assert.Equal(BranchRefId, parked.BranchRefId);
        Assert.Equal(WaitNode, parked.NodeId);

        BookmarkRow bookmark = Assert.Single(_bookmarks);
        Assert.Equal(RunId, bookmark.RunId);
        Assert.Equal(BranchRefId, bookmark.BranchRefId);
        Assert.Equal(WaitNode, bookmark.NodeId);
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000001"), bookmark.RefId);
        Assert.Equal("signal", bookmark.WakeConditionKind);
        Assert.Equal(SignalChannel.Key("approve", "scope-1"), bookmark.MatchKey);
        Assert.Equal(Now + TimeSpan.FromMinutes(5), bookmark.ExpiresAt);
        Assert.Equal("timeout", bookmark.TtlPort);
        Assert.Equal(Now, bookmark.CreatedAt);
        Assert.Equal("approve", JsonDocument.Parse(bookmark.WakeConditionJson).RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_failing_follow_up_leaves_the_branch_parked()
    {
        var wait = new NodeExecutionResult.WaitForBookmark(new HttpWakeCondition("tok-1"), Patch("waiting", true))
        {
            AfterParked = _ => throw new InvalidOperationException("send failed")
        };

        BranchResultOutcome outcome = await _handler.HandleAsync(wait, Context(Node(WaitNode), Definition([Node(WaitNode)], []), _branches.Current), CancellationToken.None);

        Assert.True(outcome.IsStopped);
        Assert.Single(_bookmarks);
        Assert.Equal(BranchStatus.Waiting, _branches.Current.Status);
    }

    [Fact]
    public void A_timer_is_woken_by_its_time_alone()
    {
        var at = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var timer = new TimerWakeCondition(at) { Ttl = TimeSpan.FromMinutes(5) };

        Assert.Equal("timer", WaitForBookmarkResultHandler.GetWakeConditionKind(timer));
        Assert.Equal(string.Empty, WaitForBookmarkResultHandler.GetWakeConditionMatchKey(timer));
        Assert.Equal(at, WaitForBookmarkResultHandler.GetWakeConditionExpiresAt(timer, Now));
    }

    /// <summary>
    /// The bookmark's match key is the key the matching channel puts on its inbound events: if the
    /// two drift, the event arrives and finds no bookmark.
    /// </summary>
    [Fact]
    public void Each_wake_condition_is_keyed_as_its_channel_keys_its_events()
    {
        var childRunId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var commandId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        Assert.Equal(SignalChannel.Key("approve", "scope-1"), WaitForBookmarkResultHandler.GetWakeConditionMatchKey(new SignalWakeCondition("approve", "scope-1")));
        Assert.Equal(HttpWakeChannel.Key("tok-1"), WaitForBookmarkResultHandler.GetWakeConditionMatchKey(new HttpWakeCondition("tok-1")));
        Assert.Equal(ChildCompletedChannel.Key(childRunId.ToString()), WaitForBookmarkResultHandler.GetWakeConditionMatchKey(new ChildRunCompletedWakeCondition(childRunId)));
        Assert.Equal(CommandChannel.Key(commandId), WaitForBookmarkResultHandler.GetWakeConditionMatchKey(new CommandOutcomeWakeCondition(commandId)));

        Assert.Equal("http", WaitForBookmarkResultHandler.GetWakeConditionKind(new HttpWakeCondition("tok-1")));
        Assert.Equal("childRunCompleted", WaitForBookmarkResultHandler.GetWakeConditionKind(new ChildRunCompletedWakeCondition(childRunId)));
        Assert.Equal("commandOutcome", WaitForBookmarkResultHandler.GetWakeConditionKind(new CommandOutcomeWakeCondition(commandId)));
        Assert.Null(WaitForBookmarkResultHandler.GetWakeConditionExpiresAt(new HttpWakeCondition("tok-1"), Now));
    }
}
