using System.Net;
using System.Text.Json;
using FluentAssertions;
using Wbskt.Api.Client;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Engine.Contracts;

namespace Wbskt.E2E.FeatureTests.Scenarios.Outage;

/// <summary>
/// Two engines on one database and broker, and the one holding the lease is killed.
/// <para>
/// What the code promises (LeaderElectionService, EngineLeadershipCoordinator, LeaderOnlyMiddleware,
/// RunRecoveryService): only the holder of the <c>engine-leader</c> lease starts its bus and serves
/// inbound calls; any other instance answers them 503 <c>ENGINE_NOT_LEADER</c> with
/// <c>Retry-After</c>, and its <c>/healthz/ready</c> is 503, which is what keeps it out of the
/// proxy's rotation. A killed leader releases nothing, so the standby takes the lease once it runs
/// out (<c>WorkflowEngine:LeaderElectionLeaseTtlSeconds</c>, 30 s by default, renewed every
/// 10 s), then starts its bus and re-dispatches the branches that were mid-execution. A parked
/// branch is not one of them: it waits on its bookmark in the database for whoever leads next.
/// </para>
/// <para>
/// What can be observed from here, and what cannot: the management host calls the one engine URL it
/// is configured with (<c>Services:WorkflowEngine</c>, the original engine), and there is no proxy in
/// this environment to move it to the new leader, so after the kill its engine calls fail until the
/// original engine is back (and fail as 500, not 503: WorkflowEngineClient does not map a refused
/// connection). So the takeover is watched, and the parked run resumed, on the standby's own inbound
/// route, which is the call a proxy routing on readiness would forward to it. Runs, history and
/// statuses are read through the management host, which reads them from the database.
/// </para>
/// Runs only with <c>E2E_OUTAGE_COMMAND</c> (see <see cref="OutageControl"/>), on its own; the
/// original engine is restored, and leading again, before the scenario ends.
/// </summary>
[Collection(EngineCollection.Name)]
public sealed class EngineFailoverE2ETests(ServicesFixture fixture)
{
    private const string SignalName = "continue";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // The rest of the dead leader's lease (at most 30 s), the standby's next renewal tick (10 s) and
    // its bus starting, with room for a slow runner.
    private static readonly TimeSpan TakeoverTimeout = TimeSpan.FromSeconds(150);

    [SkippableFact]
    public async Task OUT_ENG_01_TheStandbyTakesOver_AndTheParkedRunResumesOnce()
    {
        Skip.IfNot(E2EConfig.OutageTestsEnabled, OutageControl.SkipReason);
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        Skip.IfNot(fixture.EngineAvailable, "Engine host not directly reachable — skipping.");

        var (token, workspaceRef) = await fixture.CreateIsolatedWorkspaceAsync();
        var workflowRefId = Guid.NewGuid();
        var definition = new WorkflowBuilder($"E2E-EngineFailover-{workflowRefId:N}", workflowRefId)
            .AddManualTrigger(out var triggerNodeId)
            .AddAwaitSignal(SignalName, null, out _)
            .AddEnd()
            .BuildAndValidate();
        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, definition.Name, JsonSerializer.SerializeToElement(definition, JsonOpts));

        await OutageControl.RunWithRestoreAsync(OutageControl.EngineRestore, async () =>
        {
            await OutageControl.RunAsync(OutageControl.SecondEngineStart);

            // ── A standby refuses inbound calls and is not ready ────────────────────────────────
            // Probes go to a run that does not exist, so even a wrongly accepted one resumes nothing.
            using (var refused = await SignalStandbyAsync(Guid.NewGuid(), "probe"))
            {
                await ShouldBeNotLeaderAsync(refused);
            }

            using (var ready = await fixture.SendAsync(HttpMethod.Get, StandbyUrl("healthz/ready")))
            {
                ready.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "a standby is kept out of rotation");
            }

            // ── A run parks on its signal under the original leader ─────────────────────────────
            var runRefId = await fixture.WaitForManualTriggerLiveAsync(token, workspaceRef, publishedRef, triggerNodeId, TimeSpan.FromSeconds(60));
            await fixture.WaitForBranchParkedAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(30));
            await ServicesFixture.WaitWithoutSignalAsync(
                TimeSpan.FromSeconds(2),
                "the bookmark row is written just after BranchParked is recorded, and nothing reports it; the leader must not die in between");

            // ── The leader dies ─────────────────────────────────────────────────────────────────
            var killedPort = (await OutageControl.RunAsync(OutageControl.EngineKillLeader)).Trim();
            killedPort.Should().NotBe(new Uri(E2EConfig.StandbyEngineBaseUrl).Port.ToString(), "the second engine was started as the standby");

            // The management host is pinned to the engine that just died (see the class summary).
            using (var viaManagement = await fixture.Management.As(token).SignalRunRawAsync(
                       workspaceRef, runRefId, "probe", new SignalRequest("probe", JsonSerializer.SerializeToElement(new { }, JsonOpts))))
            {
                viaManagement.IsSuccessStatusCode.Should().BeFalse("the management host's engine is down until it is restored");
            }

            // ── Until the standby holds the lease, it keeps refusing; then it serves ────────────
            var refusals = 0;
            await ServicesFixture.PollUntilAsync(
                "the standby to take the lease and serve inbound calls",
                async () =>
                {
                    using var probe = await SignalStandbyAsync(Guid.NewGuid(), "probe");
                    if (probe.StatusCode == HttpStatusCode.ServiceUnavailable)
                    {
                        await ShouldBeNotLeaderAsync(probe);
                        refusals++;
                        return (false, $"{refusals} refusal(s) with 503 ENGINE_NOT_LEADER");
                    }

                    probe.IsSuccessStatusCode.Should().BeTrue(
                        $"the standby answers 503 ENGINE_NOT_LEADER until it leads, and the call itself after (got {(int)probe.StatusCode})");
                    return (true, "served");
                },
                TakeoverTimeout,
                TimeSpan.FromSeconds(1));

            // Readiness asserts the same leadership, plus the database and the bus.
            await ServicesFixture.PollUntilAsync(
                "the new leader to report ready",
                async () =>
                {
                    using var ready = await fixture.SendAsync(HttpMethod.Get, StandbyUrl("healthz/ready"));
                    return (ready.StatusCode == HttpStatusCode.OK, $"HTTP {(int)ready.StatusCode}");
                },
                TimeSpan.FromSeconds(60),
                TimeSpan.FromSeconds(1));

            // ── The parked branch resumes on the new leader, and the run finishes ───────────────
            await ServicesFixture.PollUntilAsync(
                $"signal '{SignalName}' to resume run {runRefId} on the new leader",
                async () =>
                {
                    using var response = await SignalStandbyAsync(runRefId, SignalName);
                    if (!response.IsSuccessStatusCode)
                    {
                        return (false, $"HTTP {(int)response.StatusCode} {await ServicesFixture.ReadErrorCodeAsync(response)}");
                    }

                    var outcome = await WbsktApiClient.ReadAsync<InboundResponse>(response);
                    return (outcome.Matched, $"outcome {outcome.Outcome}");
                },
                TimeSpan.FromSeconds(30),
                TimeSpan.FromMilliseconds(500));

            var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(60));
            summary!.Status.Should().Be("Succeeded");

            // ── Nothing ran twice ───────────────────────────────────────────────────────────────
            var runs = await fixture.ListRunsAsync(token, workspaceRef, publishedRef);
            runs.Should().ContainSingle("the failover started no second run").Which.RefId.Should().Be(runRefId);

            var history = await fixture.GetHistoryAsync(token, workspaceRef, runRefId, top: 1000);
            var kinds = string.Join(", ", history.Select(e => e.EventKind));
            history.Count(e => e.EventKind == "BranchStarted").Should().Be(1, $"the branch started once (history: {kinds})");
            history.Count(e => e.EventKind == "BranchParked").Should().Be(1, $"the branch parked once (history: {kinds})");
            history.Count(e => e.EventKind == "BranchResumed").Should().Be(1,
                $"only the signal resumed the branch; recovery on the new leader does not re-run a parked one (history: {kinds})");
        });
    }

    private static string StandbyUrl(string path) => $"{E2EConfig.StandbyEngineBaseUrl.TrimEnd('/')}/{path}";

    /// <summary>
    /// A signal straight to the second engine's inbound route. Development leaves
    /// <c>Engine:InboundApiKey</c> unset, so the route asks for no key there.
    /// </summary>
    private Task<HttpResponseMessage> SignalStandbyAsync(Guid runRefId, string signalName) =>
        fixture.SendAsync(HttpMethod.Post, StandbyUrl(EngineInboundApi.Signal(runRefId, signalName)), body: new { });

    private static async Task ShouldBeNotLeaderAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "only the leader serves inbound calls");
        response.Headers.RetryAfter.Should().NotBeNull("the caller is told when to try again");
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("ENGINE_NOT_LEADER");
    }
}
