using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

/// <summary>
/// Stands in for the engine host's event-bus-backed device command publisher.
///
/// <c>AddWorkflowEngine</c> registers <c>CommandNodeExecutor</c> but not
/// <see cref="IDeviceCommandPublisher"/>: the publisher is an outbound adapter onto RabbitMQ, so the
/// engine host supplies it in <c>Program.cs</c> rather than the workflow library owning a transport.
/// A test that resolves the executor registry therefore has to supply one itself, or the container
/// cannot construct the executor at all.
///
/// A no-op rather than a recording double because these tests are about run execution reaching a
/// terminal state, not about what was published. Anything asserting on published commands should
/// use its own recording implementation.
/// </summary>
internal sealed class NoopDeviceCommandPublisher : IDeviceCommandPublisher
{
    public Task PublishCommandAsync(
        Guid clientRefId,
        int workspaceId,
        string command,
        string payload,
        CancellationToken ct) => Task.CompletedTask;
}
