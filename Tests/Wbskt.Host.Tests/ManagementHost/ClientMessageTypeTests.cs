using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Events.Client;
using Wbskt.Management.Host.Handlers;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;

namespace Wbskt.Host.Tests.ManagementHost;

public sealed class ClientMessageTypeTests
{
    private static readonly Guid ClientRefId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly DateTime At = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    private static ClientMessageReceivedEvent Message(int clientId, string type, string payload, int secondsLater) =>
        new(ClientRefId, clientId, 7, type, payload) { CreatedAtUtc = At.AddSeconds(secondsLater) };

    [Fact]
    public void A_batch_is_summed_per_client_and_type_ignoring_case_with_the_latest_payload()
    {
        var tallies = ClientMessageTypeRecorder.Tally(
        [
            Message(1, "reading", """{"c":20}""", 0),
            Message(1, "Reading", """{"c":21}""", 2),
            Message(1, "boot", "{}", 1),
            Message(2, "reading", """{"c":5}""", 3)
        ]);

        tallies.Should().BeEquivalentTo(
        [
            new ClientMessageTypeTally(1, "Reading", 2, At.AddSeconds(2), """{"c":21}"""),
            new ClientMessageTypeTally(1, "boot", 1, At.AddSeconds(1), "{}"),
            new ClientMessageTypeTally(2, "reading", 1, At.AddSeconds(3), """{"c":5}""")
        ]);
    }

    [Fact]
    public void A_payload_too_big_to_keep_falls_back_to_the_latest_one_that_fits()
    {
        var big = $"\"{new string('x', ClientMessageTypeRecorder.MaxSamplePayloadChars)}\"";

        var tally = ClientMessageTypeRecorder.Tally([Message(1, "dump", "1", 0), Message(1, "dump", big, 1)]).Single();

        tally.Count.Should().Be(2);
        tally.LastSeenAt.Should().Be(At.AddSeconds(1));
        tally.SamplePayload.Should().Be("1");
    }

    [Fact]
    public async Task Listed_types_mark_the_platforms_own_and_parse_their_sample()
    {
        var clients = new Mock<IClientProvider>();
        clients.Setup(x => x.FindDetailByRefIdAsync(ClientRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientDetail { Id = 42, RefId = ClientRefId, WorkspaceId = 7 });
        var types = new Mock<IClientMessageTypeProvider>();
        types.Setup(x => x.GetByClientAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new ClientMessageType("reading", 1243, At, At.AddHours(1), """{"state":"open"}"""),
            new ClientMessageType("state.report", 3, At, At, "not json"),
        ]);
        var service = new ClientQueryService(clients.Object, types.Object, NullLogger<ClientQueryService>.Instance);

        var result = await service.GetMessageTypesAsync(7, ClientRefId);

        result.IsSuccess.Should().BeTrue();
        var listed = result.Value.ToList();
        listed[0].IsPlatform.Should().BeFalse();
        listed[0].Count.Should().Be(1243);
        listed[0].SamplePayload!.Value.GetProperty("state").GetString().Should().Be("open");
        listed[1].IsPlatform.Should().BeTrue();
        listed[1].SamplePayload.Should().BeNull();
    }

    [Fact]
    public async Task A_client_of_another_workspace_lists_nothing()
    {
        var clients = new Mock<IClientProvider>();
        clients.Setup(x => x.FindDetailByRefIdAsync(ClientRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientDetail { Id = 42, RefId = ClientRefId, WorkspaceId = 8 });
        var types = new Mock<IClientMessageTypeProvider>();
        var service = new ClientQueryService(clients.Object, types.Object, NullLogger<ClientQueryService>.Instance);

        var result = await service.GetMessageTypesAsync(7, ClientRefId);

        result.IsFailure.Should().BeTrue();
        types.VerifyNoOtherCalls();
    }
}
