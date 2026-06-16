using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Actions;

namespace Wbskt.Workflow.Engine.Host.Tests.NodeExecutors.Actions;

public sealed class CommandNodeExecutorTests
{
    private static readonly Guid ClientRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const int ClientId = 7;
    private const int WorkspaceId = 3;

    [Fact]
    public async Task ExecuteAsync_publishes_command_and_returns_Continue()
    {
        var publisher = new Mock<IDeviceCommandPublisher>();
        publisher.Setup(p => p.PublishCommandAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var executor = new CommandNodeExecutor(publisher.Object);
        JsonElement payload = JsonSerializer.SerializeToElement(new { temperature = 22 });
        NodeContext ctx = BuildContext("OpenVent", payload);

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var cont = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal("default", cont.OutboundPort);
        publisher.Verify(p => p.PublishCommandAsync(
            ClientRefId,
            ClientId,
            WorkspaceId,
            "OpenVent",
            payload.GetRawText(),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_returns_Continue_with_empty_patch()
    {
        var publisher = new Mock<IDeviceCommandPublisher>();
        publisher.Setup(p => p.PublishCommandAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var executor = new CommandNodeExecutor(publisher.Object);
        NodeContext ctx = BuildContext("CloseVent", null);

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var cont = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Empty(cont.LocalStatePatch);
        publisher.Verify(p => p.PublishCommandAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(),
            "CloseVent", "{}", CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_returns_Fail_with_COMMAND_NO_TARGET_when_clientRefId_missing()
    {
        var publisher = new Mock<IDeviceCommandPublisher>();
        var executor = new CommandNodeExecutor(publisher.Object);
        NodeContext ctx = BuildContextWithPayload(new Dictionary<string, JsonElement>
        {
            ["clientId"] = JsonSerializer.SerializeToElement(7),
            ["workspaceId"] = JsonSerializer.SerializeToElement(3)
        }, "OpenVent", null);

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("COMMAND_NO_TARGET", fail.ErrorCode);
        Assert.False(fail.Retryable);
        publisher.Verify(p => p.PublishCommandAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_returns_Fail_with_COMMAND_NO_TARGET_when_clientId_missing()
    {
        var publisher = new Mock<IDeviceCommandPublisher>();
        var executor = new CommandNodeExecutor(publisher.Object);
        NodeContext ctx = BuildContextWithPayload(new Dictionary<string, JsonElement>
        {
            ["clientRefId"] = JsonSerializer.SerializeToElement(ClientRefId),
            ["workspaceId"] = JsonSerializer.SerializeToElement(3)
        }, "OpenVent", null);

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("COMMAND_NO_TARGET", fail.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_returns_Fail_with_COMMAND_NO_TARGET_when_workspaceId_missing()
    {
        var publisher = new Mock<IDeviceCommandPublisher>();
        var executor = new CommandNodeExecutor(publisher.Object);
        NodeContext ctx = BuildContextWithPayload(new Dictionary<string, JsonElement>
        {
            ["clientRefId"] = JsonSerializer.SerializeToElement(ClientRefId),
            ["clientId"] = JsonSerializer.SerializeToElement(7)
        }, "OpenVent", null);

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("COMMAND_NO_TARGET", fail.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_returns_Fail_retryable_when_publisher_throws()
    {
        var publisher = new Mock<IDeviceCommandPublisher>();
        var exception = new InvalidOperationException("broker down");
        publisher.Setup(p => p.PublishCommandAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var executor = new CommandNodeExecutor(publisher.Object);
        NodeContext ctx = BuildContext("OpenVent", null);

        NodeExecutionResult result = await executor.ExecuteAsync(ctx, CancellationToken.None);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("COMMAND_PUBLISH_ERROR", fail.ErrorCode);
        Assert.Equal("broker down", fail.Message);
        Assert.True(fail.Retryable);
        Assert.Same(exception, fail.Cause);
    }



    private static NodeContext BuildContext(string command, JsonElement? payload, IIdempotencyKeyProvider? idempotency = null)
    {
        return BuildContextWithPayload(new Dictionary<string, JsonElement>
        {
            ["clientRefId"] = JsonSerializer.SerializeToElement(ClientRefId),
            ["clientId"] = JsonSerializer.SerializeToElement(ClientId),
            ["workspaceId"] = JsonSerializer.SerializeToElement(WorkspaceId)
        }, command, payload, idempotency);
    }

    private static NodeContext BuildContextWithPayload(
        Dictionary<string, JsonElement> triggerPayload,
        string command,
        JsonElement? nodePayload,
        IIdempotencyKeyProvider? idempotency = null)
    {
        var branch = new BranchContext(
            42,
            1001,
            9,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            1,
            "node-1",
            1,
            new Dictionary<string, JsonElement>(),
            triggerPayload,
            "corr-42",
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc));

        var node = new SendCommandActionNode(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "command",
            [],
            new SendCommandConfig("device-1", command, nodePayload));

        var providers = new Mock<IProviderComposite>();
        providers.SetupGet(p => p.IdempotencyKey).Returns(idempotency ?? FreshClaim().Object);

        return new NodeContext
        {
            Branch = branch,
            Node = node,
            Providers = providers.Object,
            Tick = 1,
            ParentResults = null,
            CancellationToken = CancellationToken.None, IdempotencyKey = "test-key"
        };
    }

    // Default: every UpsertPending wins its own claim (echoes the claim token), so the
    // command is treated as a first execution and published.
    private static Mock<IIdempotencyKeyProvider> FreshClaim()
    {
        var mock = new Mock<IIdempotencyKeyProvider>();
        mock.Setup(p => p.UpsertPendingAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, int runId, Guid claimToken, Guid nodeId, int attempt, CancellationToken _) => Row(key, runId, claimToken, nodeId, "Pending"));
        mock.Setup(p => p.MarkSucceededAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, string result, CancellationToken _) => Row(key, 42, Guid.NewGuid(), Guid.Empty, "Succeeded"));
        mock.Setup(p => p.MarkFailedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, string error, CancellationToken _) => Row(key, 42, Guid.NewGuid(), Guid.Empty, "Failed"));
        return mock;
    }

    // A prior execution already succeeded: UpsertPending returns a Succeeded row owned by a
    // different claim token, so the executor must skip the publish.
    private static Mock<IIdempotencyKeyProvider> AlreadySucceeded()
    {
        var mock = new Mock<IIdempotencyKeyProvider>();
        mock.Setup(p => p.UpsertPendingAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, int runId, Guid claimToken, Guid nodeId, int attempt, CancellationToken _) => Row(key, runId, Guid.NewGuid(), nodeId, "Succeeded"));
        return mock;
    }

    private static IdempotencyKeyRow Row(string key, int runId, Guid branchRefId, Guid nodeId, string status)
    {
        return new IdempotencyKeyRow
        {
            Id = 1,
            KeyValue = key,
            RunId = runId,
            BranchRefId = branchRefId,
            NodeId = nodeId,
            Attempt = 1,
            Status = status,
            ResultJson = status == "Succeeded" ? "{}" : null,
            ErrorJson = null,
            CreatedAt = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            CompletedAt = status == "Pending" ? null : new DateTime(2026, 5, 26, 12, 0, 1, DateTimeKind.Utc)
        };
    }
}
