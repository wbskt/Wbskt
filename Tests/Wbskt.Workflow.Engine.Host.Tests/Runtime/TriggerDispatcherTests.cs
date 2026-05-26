using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class TriggerDispatcherTests
{
    [Fact]
    public async Task Dispatch_resumes_bookmark_when_match_exists()
    {
        // Arrange
        var bookmarkResumer = new RecordingBookmarkResumer(new BookmarkMatchResult(true, 77, false));
        var dispatcher = CreateDispatcher(bookmarkResumer: bookmarkResumer);
        InboundEvent evt = CreateInboundEvent();

        // Act
        TriggerDispatchResult result = await dispatcher.DispatchAsync(evt, CancellationToken.None);

        // Assert
        Assert.Equal(TriggerDispatchOutcome.ResumedBookmark, result.Outcome);
        Assert.Equal(77L, result.BookmarkId);
        Assert.Equal("device:serial-1:telemetry", bookmarkResumer.Events.Single().CorrelationKey);
    }

    [Fact]
    public async Task Dispatch_returns_Idempotent_when_event_already_processed()
    {
        // Arrange
        var dispatcher = CreateDispatcher(bookmarkResumer: new RecordingBookmarkResumer(new BookmarkMatchResult(false, null, true)));

        // Act
        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        // Assert
        Assert.Equal(TriggerDispatchOutcome.Idempotent, result.Outcome);
    }

    [Fact]
    public async Task Dispatch_returns_NoRegistration_when_no_trigger_matches()
    {
        // Arrange
        var dispatcher = CreateDispatcher();

        // Act
        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        // Assert
        Assert.Equal(TriggerDispatchOutcome.NoRegistration, result.Outcome);
    }

    [Fact]
    public async Task Dispatch_starts_new_run_when_registration_exists_and_policy_permits()
    {
        // Arrange
        var registration = CreateRegistration();
        var dispatcher = CreateDispatcher(triggerRegistrationProvider: new RecordingTriggerRegistrationProvider(registration));

        // Act
        TriggerDispatchResult result = await dispatcher.DispatchAsync(CreateInboundEvent(), CancellationToken.None);

        // Assert
        Assert.Equal(TriggerDispatchOutcome.StartedRun, result.Outcome);
        Assert.Equal("device:serial-1:telemetry", result.Reason);
    }

    private static TriggerDispatcher CreateDispatcher(
        RecordingBookmarkResumer? bookmarkResumer = null,
        RecordingTriggerRegistrationProvider? triggerRegistrationProvider = null)
    {
        return new TriggerDispatcher(
            new CorrelationKeyResolver(),
            bookmarkResumer ?? new RecordingBookmarkResumer(new BookmarkMatchResult(false, null, false)),
            triggerRegistrationProvider ?? new RecordingTriggerRegistrationProvider());
    }

    private static InboundEvent CreateInboundEvent()
    {
        return new InboundEvent(
            "device",
            string.Empty,
            "evt-1",
            new Dictionary<string, JsonElement>
            {
                ["deviceSerial"] = JsonSerializer.SerializeToElement("serial-1"),
                ["payloadType"] = JsonSerializer.SerializeToElement("telemetry")
            },
            DateTime.UtcNow);
    }

    private static TriggerRegistrationRow CreateRegistration()
    {
        return new TriggerRegistrationRow
        {
            Id = 5,
            WorkflowDefinitionId = 42,
            WorkflowRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            WorkflowVersion = 3,
            TriggerNodeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            TriggerKind = "device",
            TriggerKey = "device:serial-1:telemetry",
            CorrelationExpression = null,
            ConcurrencyPolicy = "Queue",
            FilterExpression = null,
            CreatedAt = DateTime.UtcNow
        };
    }

    private sealed class RecordingBookmarkResumer(BookmarkMatchResult result) : IBookmarkResumer
    {
        public List<InboundEvent> Events { get; } = [];

        public Task<BookmarkMatchResult> MatchInboundAsync(InboundEvent evt, CancellationToken ct)
        {
            Events.Add(evt);
            return Task.FromResult(result);
        }

        public Task ResumeViaBookmarkAsync(long bookmarkId, IReadOnlyDictionary<string, JsonElement> wakePayload, CancellationToken ct)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingTriggerRegistrationProvider(params TriggerRegistrationRow[] rows) : ITriggerRegistrationProvider
    {
        public Task<TriggerRegistrationRow> InsertAsync(TriggerRegistrationRow row, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<TriggerRegistrationRow>> GetByTriggerKeyAsync(string triggerKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<TriggerRegistrationRow>> GetAllByWorkflowDefinitionIdAsync(int workflowDefinitionId, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAllByWorkflowDefinitionIdAsync(int workflowDefinitionId, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<TriggerRegistrationRow>> GetActiveByChannelAsync(string channelKind, string channelKey, CancellationToken ct)
        {
            IReadOnlyCollection<TriggerRegistrationRow> matches = rows.Where(row => row.TriggerKind == channelKind && row.TriggerKey == channelKey).ToArray();
            return Task.FromResult(matches);
        }
    }
}
