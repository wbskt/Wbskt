using System.Text.Json;
using System.Text.Json.Serialization;
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

        Assert.Equal("timer", timer.Kind);
        Assert.Equal(string.Empty, WaitForBookmarkResultHandler.GetWakeConditionMatchKey(timer));
        Assert.Equal(at, timer.ExpiresAt(Now));
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

        Assert.Null(new HttpWakeCondition("tok-1").ExpiresAt(Now));
    }

    /// <summary>
    /// Every wake condition the JSON model knows: a new one added to <see cref="WakeCondition"/>'s
    /// derived types must also say its bookmark kind and match key, or parking on it fails here.
    /// </summary>
    public static TheoryData<WakeCondition, string, string> EveryWakeCondition => new()
    {
        { new TimerWakeCondition(Now), "timer", string.Empty },
        { new SignalWakeCondition("approve", "scope-1"), "signal", SignalChannel.Key("approve", "scope-1") },
        { new HttpWakeCondition("tok-1"), "http", HttpWakeChannel.Key("tok-1") },
        { new ChildRunCompletedWakeCondition(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")), "childRunCompleted", ChildCompletedChannel.Key("cccccccc-cccc-cccc-cccc-cccccccccccc") },
        { new CommandOutcomeWakeCondition(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd")), "commandOutcome", CommandChannel.Key(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd")) }
    };

    [Theory]
    [MemberData(nameof(EveryWakeCondition))]
    public void Each_wake_condition_gives_the_kind_and_key_its_bookmark_is_parked_on(WakeCondition condition, string expectedKind, string expectedMatchKey)
    {
        Assert.Equal(expectedKind, condition.Kind);
        Assert.Equal(expectedMatchKey, WaitForBookmarkResultHandler.GetWakeConditionMatchKey(condition));
    }

    [Fact]
    public void The_cases_cover_every_derived_wake_condition_and_kind_is_its_discriminator()
    {
        Dictionary<Type, string> discriminators = typeof(WakeCondition)
            .GetCustomAttributes(typeof(JsonDerivedTypeAttribute), inherit: false)
            .Cast<JsonDerivedTypeAttribute>()
            .ToDictionary(attribute => attribute.DerivedType, attribute => (string)attribute.TypeDiscriminator!);

        var covered = EveryWakeCondition.Select(row => (WakeCondition)row[0]).ToList();

        Assert.Equal(discriminators.Keys.OrderBy(type => type.Name), covered.Select(condition => condition.GetType()).OrderBy(type => type.Name));
        Assert.All(covered, condition => Assert.Equal(discriminators[condition.GetType()], condition.Kind));
    }

    [Fact]
    public void A_ttl_sets_the_expiry_of_a_condition_that_is_not_a_timer()
    {
        var condition = new SignalWakeCondition("approve", "scope-1") { Ttl = TimeSpan.FromMinutes(5) };

        Assert.Equal(Now + TimeSpan.FromMinutes(5), condition.ExpiresAt(Now));
    }
}
