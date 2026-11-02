using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure.Devices;
using Wbskt.Management.Host.Controllers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Models;
using Wbskt.Models;
using Wbskt.Infrastructure;
using Wbskt.Primitives;
using Wbskt.Protocol;
using Wbskt.Host.Tests.Infrastructure;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>
/// <c>POST /clients/{ref}/ota</c> sends the one reserved type a caller may: only to a device that said
/// it takes updates, only for an https image with a digest, and always with an expiry.
/// </summary>
public sealed class ClientOtaTests
{
    private const int WorkspaceId = 7;
    private const int ClientId = 42;
    private const string HostId = "socket-a";
    private const string Digest = "9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08";
    private static readonly Guid ClientRefId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private readonly Mock<IClientCommandTargetProvider> _targets = new();
    private readonly Mock<IClientQueryService> _clients = new();
    private readonly Mock<IEventBus> _bus = new();
    private readonly InMemoryQueuedCommandStore _queue = new();

    private ClientsController CreateController()
    {
        var commands = new ClientCommandService(_clients.Object, TestDeviceCommands.Sender(_targets.Object, _bus.Object, _queue), _queue, _bus.Object,
            Microsoft.Extensions.Options.Options.Create(new CommandQueueOptions()), NullLogger<ClientCommandService>.Instance, new Wbskt.Management.Host.Telemetry.ManagementMetrics());
        return new ClientsController(_clients.Object, Mock.Of<IClientLifecycleService>(), commands, Mock.Of<IRegistrationPolicyService>(), Mock.Of<IEventLogService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private void Device(bool connected, params string[] system)
    {
        _targets.Setup(x => x.FindAsync(ClientRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientCommandTarget(ClientId, WorkspaceId, connected, connected ? HostId : null));
        _clients.Setup(x => x.GetDetailAsync(WorkspaceId, ClientRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClientDetailResponse>.Success(new ClientDetailResponse(
                ClientRefId, Guid.NewGuid(), "p", "board", ClientStatus.Registered, connected, null, null, null, null,
                "wbskt-esp32", "0.3.0", "esp32", [], DateTime.UtcNow, [], system)));
        _clients.Setup(x => x.EnsureClientInWorkspaceAsync(WorkspaceId, ClientRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Success(ClientId));
    }

    private Task<IActionResult?> Send(ClientOtaRequest request) =>
        CreateController().SendOta(WorkspaceId, ClientRefId, request, CancellationToken.None).ContinueWith(t => (IActionResult?)t.Result.Result);

    [Fact]
    public async Task An_update_goes_to_a_connected_device_as_sys_ota_update_with_an_expiry()
    {
        Device(connected: true, SystemCapabilities.Ota);
        ClientCommandEvent? sent = null;
        _bus.Setup(x => x.PublishAsync(It.IsAny<ClientCommandEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ClientCommandEvent, CancellationToken>((e, _) => sent = e)
            .Returns(Task.CompletedTask);

        var result = await Send(new ClientOtaRequest("https://firmware.example/relay-1.1.0.bin", Digest, "1.1.0", 1212416));

        var response = Assert.IsType<ClientCommandResponse>(Assert.IsType<AcceptedResult>(result).Value);
        response.Status.Should().Be(ClientCommandStatus.Sent);
        response.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.Add(CommandQueueOptions.MaxLifetime), TimeSpan.FromMinutes(1));
        sent.Should().NotBeNull();
        sent!.Type.Should().Be(MessageTypes.OtaUpdate);
        sent.CommandId.Should().Be(response.CommandId);
        sent.ExpiresAtUtc.Should().NotBeNull();
        var payload = JsonSerializer.Deserialize<OtaUpdatePayload>(sent.Payload)!;
        payload.Should().Be(new OtaUpdatePayload("https://firmware.example/relay-1.1.0.bin", Digest.ToLowerInvariant(), "1.1.0", 1212416));
    }

    [Fact]
    public async Task An_update_for_an_offline_device_waits_in_its_queue()
    {
        Device(connected: false, SystemCapabilities.Ota);
        var expiresAt = DateTimeOffset.UtcNow.AddHours(2);

        var result = await Send(new ClientOtaRequest("https://firmware.example/a.bin", Digest, ExpiresAt: expiresAt));

        Assert.IsType<ClientCommandResponse>(Assert.IsType<AcceptedResult>(result).Value).Status.Should().Be(ClientCommandStatus.Queued);
        var queued = _queue.Rows.Should().ContainSingle().Subject;
        queued.Type.Should().Be(MessageTypes.OtaUpdate);
        queued.ExpiresAtUtc.Should().BeCloseTo(expiresAt.UtcDateTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_newer_update_cancels_the_older_ones_still_waiting_and_leaves_other_commands()
    {
        Device(connected: false, SystemCapabilities.Ota);
        await Send(new ClientOtaRequest("https://firmware.example/1.1.bin", Digest));
        var older = _queue.Rows.Single().CommandId;
        var reboot = Guid.NewGuid();
        await _queue.EnqueueAsync(new QueuedCommand(reboot, ClientId, ClientRefId, WorkspaceId, "reboot", "{}", DateTime.UtcNow.AddHours(1)), 10, CancellationToken.None);

        var result = await Send(new ClientOtaRequest("https://firmware.example/1.2.bin", Digest));

        var newer = Assert.IsType<ClientCommandResponse>(Assert.IsType<AcceptedResult>(result).Value).CommandId;
        _queue.Rows.Select(r => r.CommandId).Should().BeEquivalentTo([reboot, newer]);
        _bus.Verify(x => x.PublishAsync(It.Is<ClientCommandCancelledEvent>(e => e.CommandId == older && e.Type == MessageTypes.OtaUpdate), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_device_that_does_not_list_ota_is_refused()
    {
        Device(connected: true);

        var result = await Send(new ClientOtaRequest("https://firmware.example/a.bin", Digest));

        ProblemAssert.Is(result, StatusCodes.Status409Conflict).Should().Be("OTA_NOT_SUPPORTED");
        _bus.Verify(x => x.PublishAsync(It.IsAny<ClientCommandEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("http://firmware.example/a.bin", Digest, "OTA_URL_INVALID")]
    [InlineData("firmware.example/a.bin", Digest, "OTA_URL_INVALID")]
    [InlineData("ftp://firmware.example/a.bin", Digest, "OTA_URL_INVALID")]
    [InlineData("https://firmware.example/a.bin", "abc", "OTA_SHA256_INVALID")]
    [InlineData("https://firmware.example/a.bin", "zz86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08", "OTA_SHA256_INVALID")]
    public async Task A_bad_url_or_digest_is_a_400(string url, string sha256, string code)
    {
        Device(connected: true, SystemCapabilities.Ota);

        var result = await Send(new ClientOtaRequest(url, sha256));

        ProblemAssert.Is(result, StatusCodes.Status400BadRequest).Should().Be(code);
        _bus.Verify(x => x.PublishAsync(It.IsAny<ClientCommandEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(5_000_000_000L)]
    public async Task A_size_that_is_not_a_byte_count_is_a_400(long size)
    {
        Device(connected: true, SystemCapabilities.Ota);

        var result = await Send(new ClientOtaRequest("https://firmware.example/a.bin", Digest, Size: size));

        ProblemAssert.Is(result, StatusCodes.Status400BadRequest).Should().Be("OTA_SIZE_INVALID");
    }

    [Fact]
    public async Task The_generic_command_endpoint_still_refuses_sys_ota_update()
    {
        Device(connected: true, SystemCapabilities.Ota);

        var result = (await CreateController().SendCommand(WorkspaceId, ClientRefId, new ClientCommandRequest(MessageTypes.OtaUpdate, "{}"), CancellationToken.None)).Result;

        ProblemAssert.Is(result, StatusCodes.Status400BadRequest).Should().Be("COMMAND_TYPE_RESERVED");
    }

    [Fact]
    public void Platform_rules_take_only_platform_commands()
    {
        DeviceMessageRules.Check(MessageTypes.OtaUpdate, "{}")!.Rule.Should().Be(DeviceMessageRule.TypeReserved);
        DeviceMessageRules.CheckPlatformCommand(MessageTypes.OtaUpdate, "{}").Should().BeNull();
        DeviceMessageRules.CheckPlatformCommand("SYS.OTA.UPDATE", "{}").Should().BeNull();
        DeviceMessageRules.CheckPlatformCommand(MessageTypes.Ping, "{}")!.Rule.Should().Be(DeviceMessageRule.TypeReserved);
        DeviceMessageRules.CheckPlatformCommand("relay.set", "{}")!.Rule.Should().Be(DeviceMessageRule.TypeReserved);
    }
}
