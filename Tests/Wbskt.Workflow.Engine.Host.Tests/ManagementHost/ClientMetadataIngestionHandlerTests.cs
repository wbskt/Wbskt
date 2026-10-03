using Wbskt.Management.Host.Models;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Management.Host.Handlers;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class ClientMetadataIngestionHandlerTests
{
    private static readonly Guid ClientRefId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const int ClientId = 42;
    private const int WorkspaceId = 7;

    private static (ClientMetadataIngestionHandler Handler, Mock<IClientProvider> Provider, Mock<IEventBus> Bus) CreateHandler()
    {
        var provider = new Mock<IClientProvider>();
        var bus = new Mock<IEventBus>();
        var handler = new ClientMetadataIngestionHandler(provider.Object, bus.Object, NullLogger<ClientMetadataIngestionHandler>.Instance);
        return (handler, provider, bus);
    }

    private static ConsumeContext<ClientMessageReceivedEvent> Context(string type, string payload)
    {
        var ctx = new Mock<ConsumeContext<ClientMessageReceivedEvent>>();
        ctx.SetupGet(x => x.Message).Returns(new ClientMessageReceivedEvent(ClientRefId, ClientId, WorkspaceId, type, payload));
        ctx.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }

    [Fact]
    public async Task Capabilities_payload_is_persisted_and_announced()
    {
        var (handler, provider, bus) = CreateHandler();
        const string payload = """{"Agent":"csharp-sdk","Version":"1.4.0","OS":"linux-arm64","Capabilities":[{"Command":"cmd.actuate","Description":"d","Parameters":[]}]}""";

        await handler.Consume(Context("capabilities", payload));

        provider.Verify(x => x.UpsertCapabilitiesAsync(ClientId, "csharp-sdk", "1.4.0", "linux-arm64",
            It.Is<string>(json => json.Contains("cmd.actuate")), It.IsAny<CancellationToken>()), Times.Once);
        bus.Verify(x => x.PublishAsync(
            It.Is<ClientCapabilitiesUpdatedEvent>(e => e.ClientRefId == ClientRefId && e.AgentName == "csharp-sdk" && e.AgentVersion == "1.4.0" && e.Platform == "linux-arm64"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Capabilities_payload_with_camel_case_keys_is_accepted()
    {
        var (handler, provider, _) = CreateHandler();
        const string payload = """{"agent":"js-sdk","version":"0.1.0","os":"browser","capabilities":[]}""";

        await handler.Consume(Context("capabilities", payload));

        provider.Verify(x => x.UpsertCapabilitiesAsync(ClientId, "js-sdk", "0.1.0", "browser", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Malformed_capabilities_payload_is_discarded()
    {
        var (handler, provider, bus) = CreateHandler();

        await handler.Consume(Context("capabilities", "{not json"));

        provider.Verify(x => x.UpsertCapabilitiesAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        bus.Verify(x => x.PublishAsync(It.IsAny<ClientCapabilitiesUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Capabilities_payload_without_agent_is_discarded()
    {
        var (handler, provider, _) = CreateHandler();

        await handler.Consume(Context("capabilities", """{"Version":"1.0","OS":"linux-x64","Capabilities":[]}"""));

        provider.Verify(x => x.UpsertCapabilitiesAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Oversized_capabilities_payload_is_discarded()
    {
        var (handler, provider, _) = CreateHandler();
        var payload = "{\"Agent\":\"" + new string('x', 33 * 1024) + "\"}";

        await handler.Consume(Context("capabilities", payload));

        provider.Verify(x => x.UpsertCapabilitiesAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task State_report_change_publishes_property_updated_with_old_value()
    {
        var (handler, provider, bus) = CreateHandler();
        provider.Setup(x => x.UpsertStateVariableAsync(ClientId, "ventPosition", "number", "75", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StateVariableUpsert(true, "0"));

        await handler.Consume(Context("state.report", """{"ventPosition":75}"""));

        bus.Verify(x => x.PublishAsync(
            It.Is<ClientPropertyUpdatedEvent>(e => e.PropertyName == "ventPosition" && e.OldValue == "0" && e.NewValue == "75"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task State_report_first_value_publishes_with_null_old_value()
    {
        var (handler, provider, bus) = CreateHandler();
        provider.Setup(x => x.UpsertStateVariableAsync(ClientId, "firmware", "string", "\"2.4.1\"", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StateVariableUpsert(true, null));

        await handler.Consume(Context("state.report", """{"firmware":"2.4.1"}"""));

        bus.Verify(x => x.PublishAsync(
            It.Is<ClientPropertyUpdatedEvent>(e => e.PropertyName == "firmware" && e.OldValue == null && e.NewValue == "\"2.4.1\""),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task State_report_unchanged_value_still_bumps_freshness_but_publishes_nothing()
    {
        var (handler, provider, bus) = CreateHandler();
        provider.Setup(x => x.UpsertStateVariableAsync(ClientId, "ventPosition", "number", "75", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StateVariableUpsert(true, "75"));

        await handler.Consume(Context("state.report", """{"ventPosition":75}"""));

        provider.Verify(x => x.UpsertStateVariableAsync(ClientId, "ventPosition", "number", "75", It.IsAny<CancellationToken>()), Times.Once);
        bus.Verify(x => x.PublishAsync(It.IsAny<ClientPropertyUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task State_report_variable_refused_at_the_cap_publishes_nothing()
    {
        var (handler, provider, bus) = CreateHandler();
        provider.Setup(x => x.UpsertStateVariableAsync(ClientId, "extra", "number", "1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StateVariableUpsert(false, null));

        await handler.Consume(Context("state.report", """{"extra":1}"""));

        bus.Verify(x => x.PublishAsync(It.IsAny<ClientPropertyUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task State_report_that_is_not_an_object_is_discarded()
    {
        var (handler, provider, _) = CreateHandler();

        await handler.Consume(Context("state.report", "[1,2,3]"));

        provider.Verify(x => x.UpsertStateVariableAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Ordinary_message_types_are_ignored()
    {
        var (handler, provider, bus) = CreateHandler();

        await handler.Consume(Context("telemetry", """{"temp":36.2}"""));

        provider.VerifyNoOtherCalls();
        bus.VerifyNoOtherCalls();
    }
}
