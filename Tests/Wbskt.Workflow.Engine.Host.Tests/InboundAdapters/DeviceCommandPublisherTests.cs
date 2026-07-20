using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Primitives;
using Wbskt.Workflow.Engine.Host.InboundAdapters;

namespace Wbskt.Workflow.Engine.Host.Tests.InboundAdapters;

public sealed class DeviceCommandPublisherTests
{
    private static readonly Guid ClientRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const int ClientId = 7;
    private const int WorkspaceId = 3;

    [Fact]
    public async Task PublishCommandAsync_resolves_client_id_and_publishes_event()
    {
        var bus = new Mock<IEventBus>();
        ClientCommandEvent? captured = null;
        bus.Setup(b => b.PublishAsync(It.IsAny<ClientCommandEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ClientCommandEvent, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var publisher = new DeviceCommandPublisher(bus.Object, CreateMapper(ClientId), NullLogger<DeviceCommandPublisher>.Instance);

        await publisher.PublishCommandAsync(ClientRefId, WorkspaceId, "OpenVent", "{}", CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(ClientRefId, captured!.ClientRefId);
        Assert.Equal(ClientId, captured.ClientId);
        Assert.Equal(WorkspaceId, captured.WorkspaceId);
        Assert.Equal("OpenVent", captured.Type);
        Assert.Equal("{}", captured.Payload);
        Assert.NotNull(captured.CommandId);
    }

    [Fact]
    public async Task PublishCommandAsync_still_publishes_when_client_ref_is_unknown()
    {
        var bus = new Mock<IEventBus>();
        ClientCommandEvent? captured = null;
        bus.Setup(b => b.PublishAsync(It.IsAny<ClientCommandEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ClientCommandEvent, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var publisher = new DeviceCommandPublisher(bus.Object, CreateMapper(0), NullLogger<DeviceCommandPublisher>.Instance);

        await publisher.PublishCommandAsync(ClientRefId, WorkspaceId, "OpenVent", "{}", CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(ClientRefId, captured!.ClientRefId);
        Assert.Equal(0, captured.ClientId);
    }

    private static IReferenceMapper CreateMapper(int clientId)
    {
        var mapper = new Mock<IReferenceMapper>();
        mapper.Setup(m => m.FindIdByRefIdAsync(ClientRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientId);
        return mapper.Object;
    }
}
