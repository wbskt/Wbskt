using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Controls;

namespace Wbskt.Workflow.Engine.Host.Tests.NodeExecutors.Controls;

public sealed class WaitForHttpNodeExecutorTests
{
    private const string ParkedKey = "__http_wait";
    private const string DeadlineKey = "__http_deadline";
    private static readonly DateTime T0 = new(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid RunRefId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [Fact]
    public async Task First_visit_parks_with_http_wake_on_author_token_and_ttl()
    {
        var executor = new WaitForHttpNodeExecutor(new MutableClock(T0));
        var node = new WaitForHttpNode { NodeId = Guid.NewGuid(), Name = "wait-http", Ports = Ports(), Config = new WaitForHttpConfig { Ttl = TimeSpan.FromMinutes(15), Token = "author-secret-token-1234567890" } };
        NodeContext ctx = CreateContext(node, new Dictionary<string, JsonElement>());

        var wait = Assert.IsType<NodeExecutionResult.WaitForBookmark>(await executor.ExecuteAsync(ctx, CancellationToken.None));
        var http = Assert.IsType<HttpWakeCondition>(wait.Condition);
        Assert.Equal("author-secret-token-1234567890", http.Token);
        Assert.Equal(TimeSpan.FromMinutes(15), http.Ttl);
        Assert.Equal("author-secret-token-1234567890", wait.LocalStatePatch[ParkedKey].GetString());
        Assert.Equal(T0.AddMinutes(15).ToString("O"), wait.LocalStatePatch[DeadlineKey].GetString());
    }

    [Fact]
    public async Task First_visit_without_token_faults()
    {
        var executor = new WaitForHttpNodeExecutor(new MutableClock(T0));
        var node = new WaitForHttpNode { NodeId = Guid.NewGuid(), Name = "wait-http", Ports = Ports(), Config = new WaitForHttpConfig { Ttl = TimeSpan.FromMinutes(15) } };
        NodeContext ctx = CreateContext(node, new Dictionary<string, JsonElement>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(ctx, CancellationToken.None));
    }

    [Fact]
    public async Task First_visit_with_author_token_parks_on_that_token()
    {
        var executor = new WaitForHttpNodeExecutor(new MutableClock(T0));
        var node = new WaitForHttpNode { NodeId = Guid.NewGuid(), Name = "wait-http", Ports = Ports(), Config = new WaitForHttpConfig { Ttl = TimeSpan.FromMinutes(15), Token = "author-secret-123" } };
        NodeContext ctx = CreateContext(node, new Dictionary<string, JsonElement>());

        var wait = Assert.IsType<NodeExecutionResult.WaitForBookmark>(await executor.ExecuteAsync(ctx, CancellationToken.None));
        var http = Assert.IsType<HttpWakeCondition>(wait.Condition);
        Assert.Equal("author-secret-123", http.Token);
        Assert.Equal("author-secret-123", wait.LocalStatePatch[ParkedKey].GetString());
    }

    [Fact]
    public async Task Resume_by_wake_continues_default()
    {
        var executor = new WaitForHttpNodeExecutor(new MutableClock(T0.AddMinutes(1)));
        var node = new WaitForHttpNode { NodeId = Guid.NewGuid(), Name = "wait-http", Ports = Ports(), Config = new WaitForHttpConfig { Ttl = TimeSpan.FromMinutes(15) } };
        NodeContext ctx = CreateContext(node, new Dictionary<string, JsonElement>
        {
            [ParkedKey] = JsonSerializer.SerializeToElement(RunRefId.ToString()),
            [DeadlineKey] = JsonSerializer.SerializeToElement(T0.AddMinutes(15).ToString("O"))
        });

        var cont = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(ctx, CancellationToken.None));
        Assert.Equal("default", cont.OutboundPort);
    }

    [Fact]
    public async Task Resume_after_deadline_continues_timeout_port()
    {
        var executor = new WaitForHttpNodeExecutor(new MutableClock(T0.AddMinutes(16)));
        var node = new WaitForHttpNode { NodeId = Guid.NewGuid(), Name = "wait-http", Ports = Ports(), Config = new WaitForHttpConfig { Ttl = TimeSpan.FromMinutes(15) } };
        NodeContext ctx = CreateContext(node, new Dictionary<string, JsonElement>
        {
            [ParkedKey] = JsonSerializer.SerializeToElement(RunRefId.ToString()),
            [DeadlineKey] = JsonSerializer.SerializeToElement(T0.AddMinutes(15).ToString("O"))
        });

        var cont = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(ctx, CancellationToken.None));
        Assert.Equal("timeout", cont.OutboundPort);
    }

    [Fact]
    public async Task Resume_with_wake_payload_promotes_body_under_wakePayload()
    {
        var executor = new WaitForHttpNodeExecutor(new MutableClock(T0.AddMinutes(1)));
        var node = new WaitForHttpNode { NodeId = Guid.NewGuid(), Name = "wait-http", Ports = Ports(), Config = new WaitForHttpConfig { Ttl = TimeSpan.FromMinutes(15) } };
        var wake = JsonSerializer.SerializeToElement(new { wakeToken = RunRefId.ToString(), body = new { status = "done" } });
        NodeContext ctx = CreateContext(node, new Dictionary<string, JsonElement>
        {
            [ParkedKey] = JsonSerializer.SerializeToElement(RunRefId.ToString()),
            [DeadlineKey] = JsonSerializer.SerializeToElement(T0.AddMinutes(15).ToString("O")),
            ["__wake"] = wake
        });

        var cont = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(ctx, CancellationToken.None));
        Assert.Equal("default", cont.OutboundPort);
        Assert.Equal("done", cont.LocalStatePatch["wakePayload"].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Resume_with_wake_payload_wins_over_elapsed_deadline()
    {
        var executor = new WaitForHttpNodeExecutor(new MutableClock(T0.AddMinutes(20)));
        var node = new WaitForHttpNode { NodeId = Guid.NewGuid(), Name = "wait-http", Ports = Ports(), Config = new WaitForHttpConfig { Ttl = TimeSpan.FromMinutes(15) } };
        NodeContext ctx = CreateContext(node, new Dictionary<string, JsonElement>
        {
            [ParkedKey] = JsonSerializer.SerializeToElement(RunRefId.ToString()),
            [DeadlineKey] = JsonSerializer.SerializeToElement(T0.AddMinutes(15).ToString("O")),
            ["__wake"] = JsonSerializer.SerializeToElement(new { body = new { ok = true } })
        });

        var cont = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(ctx, CancellationToken.None));
        Assert.Equal("default", cont.OutboundPort);
    }

    private static NodeContext CreateContext(WaitForHttpNode node, IReadOnlyDictionary<string, JsonElement> localState)
    {
        return new NodeContext
        {
            Branch = new BranchContext(42, 1001, 5, Guid.NewGuid(), 1, node.NodeId.ToString(), 1, localState, new Dictionary<string, JsonElement>(), "corr-1", T0, 9)
            {
                RunRefId = RunRefId
            },
            Node = node,
            Providers = new StubProviderComposite(),
            Tick = 1,
            ParentResults = null,
            CancellationToken = CancellationToken.None, IdempotencyKey = "test-key"
        };
    }

    private static IReadOnlyCollection<PortDefinition> Ports()
    {
        return [
            new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" },
            new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Woke" },
            new PortDefinition { PortId = "timeout", Direction = PortDirection.Output, Label = "Timeout" },
            new PortDefinition { PortId = "expired", Direction = PortDirection.Output, Label = "Expired" }
        ];
    }

    private sealed class MutableClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }

    private sealed class StubProviderComposite : IProviderComposite
    {
        public IWorkflowDefinitionProvider WorkflowDefinition => throw new NotSupportedException();
        public ITriggerRegistrationProvider TriggerRegistration => throw new NotSupportedException();
        public IBookmarkProvider Bookmark => throw new NotSupportedException();
        public ISharedVariableProvider SharedVariable => throw new NotSupportedException();
        public IIdempotencyKeyProvider IdempotencyKey => throw new NotSupportedException();
        public IPendingTriggerEventProvider PendingTriggerEvent => throw new NotSupportedException();
        public IScheduledFireProvider ScheduledFire => throw new NotSupportedException();
    }
}
