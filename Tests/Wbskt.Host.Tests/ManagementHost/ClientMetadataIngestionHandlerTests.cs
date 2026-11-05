using Wbskt.Management.Host.Models;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Management.Host.Handlers;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Host.Tests.ManagementHost;

public sealed class ClientMetadataIngestionHandlerTests
{
    private static readonly Guid ClientRefId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const int ClientId = 42;
    private const int WorkspaceId = 7;

    private static readonly DateTime ReceivedAt = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IClientReadingProvider> _readings = new();

    private (ClientMetadataIngestionHandler Handler, Mock<IClientProvider> Provider, Mock<IEventBus> Bus) CreateHandler()
    {
        var provider = new Mock<IClientProvider>();
        provider.Setup(x => x.UpsertCapabilitiesAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var bus = new Mock<IEventBus>();
        var handler = new ClientMetadataIngestionHandler(provider.Object, _readings.Object, bus.Object, NullLogger<ClientMetadataIngestionHandler>.Instance);
        return (handler, provider, bus);
    }

    private static ConsumeContext<ClientMessageReceivedEvent> Context(string type, string payload, DateTime? sentAt = null)
    {
        var ctx = new Mock<ConsumeContext<ClientMessageReceivedEvent>>();
        ctx.SetupGet(x => x.Message).Returns(new ClientMessageReceivedEvent(ClientRefId, ClientId, WorkspaceId, type, payload)
        {
            CreatedAtUtc = ReceivedAt,
            SentAtUtc = sentAt
        });
        ctx.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }

    [Fact]
    public async Task An_older_capabilities_report_is_stored_by_its_receive_time_and_not_announced_when_refused()
    {
        var (handler, provider, bus) = CreateHandler();
        provider.Setup(x => x.UpsertCapabilitiesAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), ReceivedAt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await handler.Consume(Context("capabilities", """{"Agent":"wbskt-esp32","Version":"0.3.0","OS":"esp32","Capabilities":[],"System":["ota"]}"""));

        bus.Verify(x => x.PublishAsync(It.IsAny<ClientCapabilitiesUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_announcement_carries_the_whole_report()
    {
        var (handler, _, bus) = CreateHandler();

        await handler.Consume(Context("capabilities",
            """{"Agent":"wbskt-esp32","Version":"0.3.0","OS":"esp32","Capabilities":[{"Command":"relay.set","Description":"d","Parameters":[]}],"System":["OTA"],"FirmwareVersion":"1.2.0"}"""));

        bus.Verify(x => x.PublishAsync(It.Is<ClientCapabilitiesUpdatedEvent>(e =>
            e.FirmwareVersion == "1.2.0" && e.System!.SequenceEqual(new[] { "ota" }) && e.Commands!.Single().Command == "relay.set"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Capabilities_payload_is_persisted_and_announced()
    {
        var (handler, provider, bus) = CreateHandler();
        const string payload = """{"Agent":"csharp-sdk","Version":"1.4.0","OS":"linux-arm64","Capabilities":[{"Command":"cmd.actuate","Description":"d","Parameters":[]}]}""";

        await handler.Consume(Context("capabilities", payload));

        provider.Verify(x => x.UpsertCapabilitiesAsync(ClientId, "csharp-sdk", "1.4.0", "linux-arm64",
            It.Is<string>(json => json.Contains("cmd.actuate")), null, null, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
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

        provider.Verify(x => x.UpsertCapabilitiesAsync(ClientId, "js-sdk", "0.1.0", "browser", It.IsAny<string>(), null, null, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Capabilities_system_section_is_kept_lower_case_and_once_each()
    {
        var (handler, provider, _) = CreateHandler();
        const string payload = """{"agent":"wbskt-esp32","version":"0.3.0","os":"esp32","capabilities":[],"system":["ota","OTA"," ",null]}""";

        await handler.Consume(Context("capabilities", payload));

        provider.Verify(x => x.UpsertCapabilitiesAsync(ClientId, "wbskt-esp32", "0.3.0", "esp32", "[]", """["ota"]""", null, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Capabilities_firmware_version_is_trimmed_and_capped()
    {
        var (handler, provider, _) = CreateHandler();
        var payload = "{\"agent\":\"wbskt-esp32\",\"version\":\"0.3.0\",\"os\":\"esp32\",\"capabilities\":[],\"firmwareVersion\":\"  " + new string('9', 60) + "  \"}";

        await handler.Consume(Context("capabilities", payload));

        provider.Verify(x => x.UpsertCapabilitiesAsync(ClientId, "wbskt-esp32", "0.3.0", "esp32", "[]", null, new string('9', 50), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Blank_firmware_version_is_stored_as_none()
    {
        var (handler, provider, _) = CreateHandler();

        await handler.Consume(Context("capabilities", """{"agent":"wbskt-esp32","version":"0.3.0","os":"esp32","capabilities":[],"firmwareVersion":"  "}"""));

        provider.Verify(x => x.UpsertCapabilitiesAsync(ClientId, "wbskt-esp32", "0.3.0", "esp32", "[]", null, null, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Malformed_capabilities_payload_is_discarded()
    {
        var (handler, provider, bus) = CreateHandler();

        await handler.Consume(Context("capabilities", "{not json"));

        provider.Verify(x => x.UpsertCapabilitiesAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        bus.Verify(x => x.PublishAsync(It.IsAny<ClientCapabilitiesUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Capabilities_payload_without_agent_is_discarded()
    {
        var (handler, provider, _) = CreateHandler();

        await handler.Consume(Context("capabilities", """{"Version":"1.0","OS":"linux-x64","Capabilities":[]}"""));

        provider.Verify(x => x.UpsertCapabilitiesAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Oversized_capabilities_payload_is_discarded()
    {
        var (handler, provider, _) = CreateHandler();
        var payload = "{\"Agent\":\"" + new string('x', 33 * 1024) + "\"}";

        await handler.Consume(Context("capabilities", payload));

        provider.Verify(x => x.UpsertCapabilitiesAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Answers every reported variable alike, in the order given, as the procedure does.
    private static void Report(Mock<IClientProvider> provider, bool stored, string? oldValueJson)
    {
        provider.Setup(x => x.ReportStateVariablesAsync(ClientId, It.IsAny<IReadOnlyList<StateVariableValue>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, IReadOnlyList<StateVariableValue> variables, int _, CancellationToken _) =>
                variables.Select(v => new StateVariableUpsert(v.Name, stored, oldValueJson)).ToList());
    }

    [Fact]
    public async Task A_state_report_is_stored_in_one_call_with_one_row_per_name_holding_the_last_value()
    {
        var (handler, provider, bus) = CreateHandler();
        Report(provider, stored: true, oldValueJson: null);

        await handler.Consume(Context("state.report", """{"temp":1,"door":"open","Temp":2}"""));

        provider.Verify(x => x.ReportStateVariablesAsync(ClientId,
            It.Is<IReadOnlyList<StateVariableValue>>(v => v.SequenceEqual(new[] { new StateVariableValue("temp", "number", "2"), new StateVariableValue("door", "string", "\"open\"") })),
            ClientMetadataIngestionHandler.MaxStateVariablesPerClient, It.IsAny<CancellationToken>()), Times.Once);
        bus.Verify(x => x.PublishAsync(It.IsAny<ClientPropertyUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task State_report_change_publishes_property_updated_with_old_value()
    {
        var (handler, provider, bus) = CreateHandler();
        Report(provider, stored: true, oldValueJson: "0");

        await handler.Consume(Context("state.report", """{"ventPosition":75}"""));

        bus.Verify(x => x.PublishAsync(
            It.Is<ClientPropertyUpdatedEvent>(e => e.PropertyName == "ventPosition" && e.OldValue == "0" && e.NewValue == "75"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task State_report_first_value_publishes_with_null_old_value()
    {
        var (handler, provider, bus) = CreateHandler();
        Report(provider, stored: true, oldValueJson: null);

        await handler.Consume(Context("state.report", """{"firmware":"2.4.1"}"""));

        bus.Verify(x => x.PublishAsync(
            It.Is<ClientPropertyUpdatedEvent>(e => e.PropertyName == "firmware" && e.OldValue == null && e.NewValue == "\"2.4.1\""),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task State_report_unchanged_value_still_bumps_freshness_but_publishes_nothing()
    {
        var (handler, provider, bus) = CreateHandler();
        Report(provider, stored: true, oldValueJson: "75");

        await handler.Consume(Context("state.report", """{"ventPosition":75}"""));

        provider.Verify(x => x.ReportStateVariablesAsync(ClientId,
            It.Is<IReadOnlyList<StateVariableValue>>(v => v.SequenceEqual(new[] { new StateVariableValue("ventPosition", "number", "75") })),
            ClientMetadataIngestionHandler.MaxStateVariablesPerClient, It.IsAny<CancellationToken>()), Times.Once);
        bus.Verify(x => x.PublishAsync(It.IsAny<ClientPropertyUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task State_report_variable_refused_at_the_cap_publishes_nothing()
    {
        var (handler, provider, bus) = CreateHandler();
        Report(provider, stored: false, oldValueJson: null);

        await handler.Consume(Context("state.report", """{"extra":1}"""));

        bus.Verify(x => x.PublishAsync(It.IsAny<ClientPropertyUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task State_report_that_is_not_an_object_is_discarded()
    {
        var (handler, provider, _) = CreateHandler();

        await handler.Consume(Context("state.report", "[1,2,3]"));

        provider.Verify(x => x.ReportStateVariablesAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<StateVariableValue>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Ordinary_message_types_are_ignored()
    {
        var (handler, provider, bus) = CreateHandler();

        await handler.Consume(Context("telemetry", """{"temp":36.2}"""));

        provider.VerifyNoOtherCalls();
        bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task State_report_numbers_go_to_the_readings_history_at_the_clients_time()
    {
        var (handler, provider, _) = CreateHandler();
        Report(provider, stored: true, oldValueJson: null);
        var sentAt = ReceivedAt.AddSeconds(-2);

        await handler.Consume(Context("state.report", """{"temp":4.5,"battery":87,"door":"open","on":true,"pos":[1,2]}""", sentAt));

        _readings.Verify(x => x.InsertAsync(ClientId, sentAt, ReceivedAt, false,
            It.Is<IReadOnlyCollection<ClientReadingValue>>(r => r.SequenceEqual(new[] { new ClientReadingValue("temp", 4.5), new ClientReadingValue("battery", 87) })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_report_sent_from_the_offline_buffer_is_marked_late()
    {
        var (handler, provider, _) = CreateHandler();
        Report(provider, stored: true, oldValueJson: null);
        var sentAt = ReceivedAt.AddMinutes(-20);

        await handler.Consume(Context("state.report", """{"temp":3}""", sentAt));

        _readings.Verify(x => x.InsertAsync(ClientId, sentAt, ReceivedAt, true, It.IsAny<IReadOnlyCollection<ClientReadingValue>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_report_without_a_sent_time_counts_when_it_arrived()
    {
        var (handler, provider, _) = CreateHandler();
        Report(provider, stored: true, oldValueJson: null);

        await handler.Consume(Context("state.report", """{"temp":3}"""));

        _readings.Verify(x => x.InsertAsync(ClientId, ReceivedAt, ReceivedAt, false, It.IsAny<IReadOnlyCollection<ClientReadingValue>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Numbers_refused_at_the_variable_cap_are_not_recorded()
    {
        var (handler, provider, _) = CreateHandler();
        Report(provider, stored: false, oldValueJson: null);

        await handler.Consume(Context("state.report", """{"extra":1}"""));

        _readings.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_report_with_no_numbers_records_no_readings()
    {
        var (handler, provider, _) = CreateHandler();
        Report(provider, stored: true, oldValueJson: null);

        await handler.Consume(Context("state.report", """{"door":"open"}"""));

        _readings.VerifyNoOtherCalls();
    }
}
