using System.Net;
using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Expressions;

namespace Wbskt.E2E.FeatureTests.Scenarios.Triggers;

/// <summary>
/// A device trigger with a hold time: a reading that stays out of range for the whole hold starts one
/// run, however many more out-of-range readings follow; a reading that drops back in range first
/// starts nothing. A hold needs one message type and a filter: the management API refuses either
/// missing, and a device's other message types never reset a hold on one type.
/// </summary>
[Collection(EngineCollection.Name)]
public sealed class ClientHoldTriggerE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task A_reading_that_stays_too_warm_starts_one_run()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, workspaceRef, clientRefId, secret, deviceName) = await RegisterDeviceAsync("warm");
        var workflowRefId = await PublishAsync(token, workspaceRef, clientRefId, holdSeconds: 2, Celsius(BinaryOperator.GreaterThan, 8));
        // A sentinel on the same device that only a reading above 11 starts holding, for 4 seconds.
        // The last reading below is the only one that warm, so the sentinel's run starts after every
        // reading before it has been recorded and any hold they could have started (2 seconds at
        // most) was due: by then a second run of the trigger under test would have started.
        var sentinelRefId = await PublishAsync(token, workspaceRef, clientRefId, holdSeconds: 4, Celsius(BinaryOperator.GreaterThan, 11));
        await using var client = await ConnectAsync(clientRefId, secret, deviceName);

        for (int i = 0; i < 4; i++)
        {
            await client.SendAsync("temperature", new { celsius = 10 });
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, workflowRefId, TimeSpan.FromSeconds(60));
        runRefId.Should().NotBe(Guid.Empty, "the reading stayed above the limit for the whole hold");

        // Still too warm: the trigger has fired for this hold and stays quiet.
        for (int i = 0; i < 4; i++)
        {
            await client.SendAsync("temperature", new { celsius = 11 });
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
        await client.SendAsync("temperature", new { celsius = 12 });

        await fixture.WaitForFirstRunAsync(token, workspaceRef, sentinelRefId, TimeSpan.FromSeconds(60));

        var runs = await fixture.ListRunsAsync(token, workspaceRef, workflowRefId);
        runs.Should().HaveCount(1, "a reading that stays out of range alerts once, not on every message");
    }

    [SkippableFact]
    public async Task A_reading_that_recovers_within_the_hold_starts_nothing()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, workspaceRef, clientRefId, secret, deviceName) = await RegisterDeviceAsync("recovers");
        var workflowRefId = await PublishAsync(token, workspaceRef, clientRefId, holdSeconds: 10, Celsius(BinaryOperator.GreaterThan, 8));
        // A sentinel on the same device that holds on an in-range reading, for 12 seconds. The
        // in-range reading comes 3 seconds after the warm one, so the sentinel is due 15 seconds after
        // the warm reading, past the 10 seconds the trigger under test would have needed: once the
        // sentinel has run, the hold ticker has looked at that hold too.
        var sentinelRefId = await PublishAsync(token, workspaceRef, clientRefId, holdSeconds: 12, Celsius(BinaryOperator.LessThan, 8));
        await using var client = await ConnectAsync(clientRefId, secret, deviceName);

        // Warm for a few seconds, then back in range well within the hold.
        await client.SendAsync("temperature", new { celsius = 10 });
        await Task.Delay(TimeSpan.FromSeconds(3));
        await client.SendAsync("temperature", new { celsius = 5 });

        await fixture.WaitForFirstRunAsync(token, workspaceRef, sentinelRefId, TimeSpan.FromSeconds(60));

        var runs = await fixture.ListRunsAsync(token, workspaceRef, workflowRefId);
        runs.Should().BeEmpty("the reading came back in range before the hold ran out");
    }

    [SkippableFact]
    public async Task A_message_of_another_type_does_not_reset_the_hold()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, workspaceRef, clientRefId, secret, deviceName) = await RegisterDeviceAsync("other-type");
        var workflowRefId = await PublishAsync(token, workspaceRef, clientRefId, holdSeconds: 3, Celsius(BinaryOperator.GreaterThan, 8));
        await using var client = await ConnectAsync(clientRefId, secret, deviceName);

        // One warm reading, then only heartbeats, which carry no celsius. The trigger listens for
        // temperature alone, so the heartbeats are never checked against its filter and the hold runs out.
        await client.SendAsync("temperature", new { celsius = 10 });
        for (int i = 0; i < 5; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            await client.SendAsync("heartbeat", new { uptime = i });
        }

        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, workflowRefId, TimeSpan.FromSeconds(60));
        runRefId.Should().NotBe(Guid.Empty, "a heartbeat is another message type, so it cannot reset a hold on temperature");
    }

    [SkippableFact]
    public async Task A_hold_on_any_message_type_is_refused()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, workspaceRef) = await fixture.CreateIsolatedWorkspaceAsync();

        var (validation, publish) = await ValidateAndPublishAsync(token, workspaceRef, "*", 60, Celsius(BinaryOperator.LessThan, 2));

        validation.IsValid.Should().BeFalse();
        validation.Issues.Should().Contain(i => i.Code == "HOLD_REQUIRES_MESSAGE_TYPE" && i.Severity == "Error" && i.NodeId != null,
            "every other message type would fail the filter and reset the hold");
        publish.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await publish.Content.ReadAsStringAsync()).Should().Contain("HOLD_REQUIRES_MESSAGE_TYPE");
    }

    [SkippableFact]
    public async Task A_hold_without_a_filter_is_refused()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, workspaceRef) = await fixture.CreateIsolatedWorkspaceAsync();

        var (validation, publish) = await ValidateAndPublishAsync(token, workspaceRef, "temperature", 60, filter: null);

        validation.IsValid.Should().BeFalse();
        validation.Issues.Should().Contain(i => i.Code == "HOLD_REQUIRES_FILTER" && i.Severity == "Error" && i.NodeId != null,
            "with no condition there is nothing to hold");
        publish.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await publish.Content.ReadAsStringAsync()).Should().Contain("HOLD_REQUIRES_FILTER");
    }

    [SkippableFact]
    public async Task Any_message_type_with_a_filter_and_no_hold_is_accepted()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, workspaceRef, clientRefId, _, _) = await RegisterDeviceAsync("any-type");

        var (validation, publish) = await ValidateAndPublishAsync(token, workspaceRef, "*", 0, Celsius(BinaryOperator.LessThan, 2), clientRefId);

        validation.Issues.Should().NotContain(i => i.Severity == "Error");
        publish.IsSuccessStatusCode.Should().BeTrue("only the hold time needs one message type, not the filter");
    }

    private async Task<(string Token, Guid WorkspaceRef, Guid ClientRefId, string Secret, string DeviceName)> RegisterDeviceAsync(string label)
    {
        var (token, workspaceRef) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);
        var deviceName = $"e2e-hold-{label}-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, deviceName);
        return (token, workspaceRef, clientRefId, secret, deviceName);
    }

    // payload.celsius {op} {limit}
    private static WorkflowExpression Celsius(BinaryOperator op, int limit) =>
        new BinaryExpression(
            new BranchStateRefExpression("payload.celsius"),
            op,
            new LiteralExpression(limit));

    private async Task<Guid> PublishAsync(string token, Guid workspaceRef, Guid clientRefId, int holdSeconds, WorkflowExpression filter)
    {
        var workflowRefId = Guid.NewGuid();

        WorkflowDefinition definition = new WorkflowBuilder($"E2E-Hold-{workflowRefId:N}", workflowRefId)
            .AddClientTrigger(clientRefId.ToString(), "temperature", WorkflowConcurrencyPolicy.AllowParallel, null, out _, filter: filter, holdSeconds: holdSeconds)
            .AddDelay(TimeSpan.FromMilliseconds(10))
            .BuildAndValidate();

        return await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, $"E2E-Hold-{workflowRefId:N}",
            JsonSerializer.SerializeToElement(definition, JsonOpts));
    }

    // Validates, then publishes, a device trigger the builder would refuse, as a hand-edited definition would be.
    private async Task<(WorkflowValidationResponse Validation, HttpResponseMessage Publish)> ValidateAndPublishAsync(
        string token, Guid workspaceRef, string type, int holdSeconds, WorkflowExpression? filter, Guid? clientRefId = null)
    {
        var workflowRefId = Guid.NewGuid();
        var name = $"E2E-Hold-Rules-{workflowRefId:N}";
        WorkflowDefinition definition = new WorkflowBuilder(name, workflowRefId)
            .AddClientTrigger((clientRefId ?? Guid.NewGuid()).ToString(), type, WorkflowConcurrencyPolicy.AllowParallel, null, out _, filter: filter, holdSeconds: holdSeconds)
            .AddDelay(TimeSpan.FromMilliseconds(10))
            .Build();
        var request = new WorkflowPublishRequest(workflowRefId, name, null, definition);
        var management = fixture.Management.As(token);
        var validation = await management.ValidateWorkflowAsync(workspaceRef, request);
        var publish = await management.PublishWorkflowRawAsync(workspaceRef, request);
        return (validation, publish);
    }

    private static async Task<WbsktClient> ConnectAsync(Guid clientRefId, string secret, string deviceName)
    {
        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, deviceName, null);
        var client = new WbsktClient(clientConfig, storage);
        await client.StartAsync();
        return client;
    }
}
