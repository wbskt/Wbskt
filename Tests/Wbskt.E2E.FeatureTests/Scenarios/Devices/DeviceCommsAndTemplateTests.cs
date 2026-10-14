using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Management.Models;
using Wbskt.Models;

namespace Wbskt.E2E.FeatureTests.Scenarios.Devices;

/// <summary>
/// A device's comms feed, and replacing and deleting a message template: routes no other scenario
/// reaches, each hit once through the typed client.
///
/// Requires the auth, management and socket hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(DevicesCollection.Name)]
public sealed class DeviceCommsAndTemplateTests(ServicesFixture fixture)
{
    [SkippableFact]
    public async Task DEV_COMMS_01_AConnectedDevice_HasCommsEntries()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: true);
        var deviceName = $"e2e-comms-{Guid.NewGuid():N}";
        var (clientRef, secret) = await fixture.RegisterClientAsync(pin, deviceName);

        var config = new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, deviceName, null);
        await using var device = new WbsktClient(config, new InMemoryClientStorage(clientRef, secret));
        await device.StartAsync();
        await device.SendAsync("telemetry", new { sensor = "comms", value = 1 });

        // Comms are event-log rows, written in batches off the bus.
        var page = await ServicesFixture.PollUntilAsync<Page<EventLogResponse>>(
            $"comms entries for client {clientRef}",
            async () =>
            {
                var comms = await fixture.Management.As(token).GetClientCommsAsync(workspace, clientRef);
                return (comms.Items.Any(), comms, $"{comms.Items.Count()} entries");
            },
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(2));

        page.Items.Should().Contain(e => e.ClientRefId == clientRef, "the feed is this client's");
        page.Items.Should().OnlyContain(e => !string.IsNullOrEmpty(e.EventName));
    }

    [SkippableFact]
    public async Task DEV_TPL_01_ATemplate_IsReplacedThenDeleted()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.CreateIsolatedWorkspaceAsync();
        var management = fixture.Management.As(token);

        var created = await management.CreateMessageTemplateAsync(
            workspace, new MessageTemplateRequest($"tpl-{Guid.NewGuid():N}", "e2e.blink", "{\"on\":true}", null));

        string renamed = $"tpl-renamed-{Guid.NewGuid():N}";
        await management.UpdateMessageTemplateAsync(
            workspace, created.RefId, new MessageTemplateRequest(renamed, "e2e.blink", "{\"on\":false}", null));

        var afterUpdate = (await management.GetMessageTemplatesAsync(workspace)).Items.Should()
            .ContainSingle(t => t.RefId == created.RefId).Subject;
        afterUpdate.Name.Should().Be(renamed);
        afterUpdate.PayloadJson.Should().Contain("false");

        await management.DeleteMessageTemplateAsync(workspace, created.RefId);

        (await management.GetMessageTemplatesAsync(workspace)).Items.Should().NotContain(t => t.RefId == created.RefId);
    }
}
