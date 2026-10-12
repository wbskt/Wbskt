using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Engine.Contracts;

namespace Wbskt.Workflow.Integrations;

/// <summary>
/// One integration type's connection to its service. <see cref="TestAsync"/> is what "Send test"
/// in the console runs: it proves the credential works (and, with options, that it can reach a
/// chat or a mailbox), and reports what it learned about the account. Each channel also has its own
/// send, which its step's executor calls.
/// </summary>
internal interface IIntegrationChannel
{
    IntegrationType Type { get; }

    Task<ChannelResult> TestAsync(ResolvedIntegration integration, IntegrationTestOptions? options, CancellationToken ct);
}
