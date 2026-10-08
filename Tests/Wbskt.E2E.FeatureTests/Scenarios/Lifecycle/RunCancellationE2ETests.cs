using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.E2E.FeatureTests.Scenarios.Lifecycle;

/// <summary>
/// Cancelling a run. The management host does not cancel anything itself: it checks the run, sends the
/// engine a CancelWorkflowRun command and answers 202, and the engine moves the run to 'Cancelled'
/// shortly after. So every scenario that expects a cancelled run polls for it rather than reading it
/// straight after the request.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class RunCancellationE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>Long enough for the command to cross the broker and the engine to finalize the run.</summary>
    private static readonly TimeSpan CancelledWithin = TimeSpan.FromSeconds(30);

    [SkippableFact]
    public async Task CancelRun_TransitionsToCancelled_AndDeletesBookmarks()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, "DeviceCancellation1");

        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(
            BaseApiUrl: E2EConfig.ManagementBaseUrl,
            BaseSocketUrl: E2EConfig.SocketWsBaseUrl,
            DeviceName: "DeviceCancellation1",
            PolicyPin: null);

        var workflowRefId = Guid.NewGuid();
        var deviceRefStr = clientRefId.ToString();
        var builder = new WorkflowBuilder($"E2E-RunCancel-{workflowRefId:N}", workflowRefId)
            .AddClientTrigger(deviceRefStr, "telemetry", WorkflowConcurrencyPolicy.AllowParallel, out var triggerNodeId)
            .AddAwaitSignal("continue", TimeSpan.FromSeconds(60), awaitSignal =>
            {
                awaitSignal.OnSuccess(b => b.AddClientMessage(deviceRefStr, "SignalFired", "Signal Fired", null));
                awaitSignal.OnTimeout(b => b.AddFailRun("Should not have timed out"));
            });

        var definition = builder.BuildAndValidate();
        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, definition.Name,
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        var commands = new List<string>();
        var commandLock = new object();
        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        wbsktClient.OnMessageReceived += (action, _, _) =>
        {
            lock (commandLock)
            {
                commands.Add(action);
            }
        };
        await wbsktClient.StartAsync();

        await wbsktClient.SendAsync("telemetry", new { });

        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, publishedRef, TimeSpan.FromSeconds(30));
        runRefId.Should().NotBe(Guid.Empty);

        // Run is now waiting at the AwaitSignal bookmark. The cancel is accepted, not applied: the
        // engine carries it out after the request returns.
        using (var cancel = await fixture.CancelRunRawAsync(token, workspaceRef, runRefId))
        {
            cancel.StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, CancelledWithin);
        summary.Should().NotBeNull();
        summary!.Status.Should().Be("Cancelled");

        // Try to send the signal; it should fail or not trigger anything
        var (matched, _) = await fixture.SendSignalAsync(token, workspaceRef, runRefId, "continue");
        matched.Should().BeFalse("because the run was cancelled and the signal bookmark should have been deleted");

        // Wait a bit to ensure no command is received
        await Task.Delay(2000);

        lock (commandLock)
        {
            commands.Should().BeEmpty("because the run was cancelled and the signal should not resume it");
        }
    }

    [SkippableFact]
    public async Task CancelRun_OfARunThatAlreadyFinished_Returns409()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
        var (publishedRef, triggerNodeId) = await PublishTriggerOnlyAsync(token, workspaceRef);

        var runRefId = await StartAndWaitForRunAsync(token, workspaceRef, publishedRef, triggerNodeId);
        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(30));
        summary!.Status.Should().Be("Succeeded");

        // The management host reads the run before sending anything, so a finished run is still
        // refused outright rather than accepted and quietly ignored by the engine.
        using var cancel = await fixture.CancelRunRawAsync(token, workspaceRef, runRefId);

        cancel.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ServicesFixture.ReadErrorCodeAsync(cancel)).Should().Be("RUN_NOT_CANCELLABLE");
    }

    [SkippableFact]
    public async Task CancelRun_OfAnotherWorkspacesRun_Returns404()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
        var (publishedRef, triggerNodeId) = await PublishAwaitingSignalAsync(token, workspaceRef);
        var runRefId = await StartAndWaitForRunAsync(token, workspaceRef, publishedRef, triggerNodeId);

        // A member of a different workspace, naming that workspace in the route: the run is not there.
        var outsider = await fixture.CreateUserAsync();
        var outsiderWorkspace = await fixture.CreateWorkspaceAsync(outsider.Token);

        using var cancel = await fixture.CancelRunRawAsync(outsider.Token, outsiderWorkspace, runRefId);

        cancel.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ServicesFixture.ReadErrorCodeAsync(cancel)).Should().Be("RUN_NOT_FOUND");

        // And it was left alone: the owner can still cancel it.
        using var ownCancel = await fixture.CancelRunRawAsync(token, workspaceRef, runRefId);
        ownCancel.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, CancelledWithin))!.Status.Should().Be("Cancelled");
    }

    /// <summary>
    /// Deleting a workspace sends the engine a cancel for each run still going. The workspace's runs
    /// cannot be read through the API once it is gone, so this asks the engine directly whether the
    /// parked run's signal bookmark still exists: a cancelled run's bookmarks are deleted.
    /// </summary>
    [SkippableFact]
    public async Task DeletingTheWorkspace_CancelsItsRunsInFlight()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        Skip.IfNot(fixture.EngineAvailable, "Engine host not directly reachable — skipping.");

        var user = await fixture.CreateUserAsync();
        var workspaceRef = await fixture.CreateWorkspaceAsync(user.Token);
        var (_, pin) = await fixture.CreatePolicyAsync(user.Token, workspaceRef);
        var (publishedRef, triggerNodeId) = await PublishAwaitingSignalAsync(user.Token, workspaceRef);
        var runRefId = await StartAndWaitForRunAsync(user.Token, workspaceRef, publishedRef, triggerNodeId);

        using (var deleted = await fixture.SendAsync(HttpMethod.Delete, ServicesFixture.AuthUrl($"/api/workspaces/{workspaceRef}"), user.Token))
        {
            deleted.EnsureSuccessStatusCode();
        }

        // The management host retires the workspace in response to an event, disabling its policy
        // before it sends the cancels; once the PIN stops working, the commands are on their way.
        var retired = await ServicesFixture.PollAsync(
            async () =>
            {
                using var registration = await fixture.SendAsync(
                    HttpMethod.Post,
                    ServicesFixture.ManagementUrl("/api/client-registrations/initiate"),
                    body: new { Pin = pin, Name = $"e2e-late-{Guid.NewGuid():N}" });
                return !registration.IsSuccessStatusCode;
            },
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(500));
        retired.Should().BeTrue("the deleted workspace is retired by the management host");

        // Asking is not free: a signal that still matched would resume the run. So give the engine
        // time to take the command first, then ask once.
        await Task.Delay(TimeSpan.FromSeconds(5));

        using var signal = await fixture.SendAsync(
            HttpMethod.Post,
            $"{E2EConfig.WorkflowBaseUrl}/api/inbound/signal/{runRefId}/continue",
            body: new { });
        signal.EnsureSuccessStatusCode();
        var outcome = await signal.Content.ReadFromJsonAsync<EngineSignalDto>(JsonOpts);

        outcome!.Matched.Should().BeFalse("the run was cancelled when its workspace was deleted, which deletes its bookmarks");
    }

    private async Task<(Guid PublishedRef, Guid TriggerNodeId)> PublishTriggerOnlyAsync(string token, Guid workspaceRef)
    {
        var workflowRefId = Guid.NewGuid();
        var definition = new WorkflowBuilder($"E2E-RunCancel-Done-{workflowRefId:N}", workflowRefId)
            .AddManualTrigger(out var triggerNodeId)
            .BuildAndValidate();

        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, definition.Name,
            JsonSerializer.SerializeToElement(definition, JsonOpts));
        return (publishedRef, triggerNodeId);
    }

    /// <summary>A manual workflow that parks on a signal nobody sends, so its run stays in flight.</summary>
    private async Task<(Guid PublishedRef, Guid TriggerNodeId)> PublishAwaitingSignalAsync(string token, Guid workspaceRef)
    {
        var workflowRefId = Guid.NewGuid();
        var definition = new WorkflowBuilder($"E2E-RunCancel-Parked-{workflowRefId:N}", workflowRefId)
            .AddManualTrigger(out var triggerNodeId)
            .AddAwaitSignal("continue", TimeSpan.FromMinutes(5), awaitSignal =>
            {
                awaitSignal.OnSuccess(b => b.AddEnd());
                awaitSignal.OnTimeout(b => b.AddFailRun("Should have been cancelled before timing out"));
            })
            .BuildAndValidate();

        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, definition.Name,
            JsonSerializer.SerializeToElement(definition, JsonOpts));
        return (publishedRef, triggerNodeId);
    }

    private async Task<Guid> StartAndWaitForRunAsync(string token, Guid workspaceRef, Guid publishedRef, Guid triggerNodeId)
    {
        await fixture.StartManualRunAsync(token, workspaceRef, publishedRef, triggerNodeId.ToString(), idempotencyKey: null);
        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, publishedRef, TimeSpan.FromSeconds(30));
        runRefId.Should().NotBe(Guid.Empty);
        return runRefId;
    }

    private sealed record EngineSignalDto(string Outcome, bool Matched);
}
