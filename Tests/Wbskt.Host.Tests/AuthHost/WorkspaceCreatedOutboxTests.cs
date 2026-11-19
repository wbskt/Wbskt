using System.Text.Json;
using Moq;
using Wbskt.Auth.Host.Services;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Host.Tests.AuthHost;

/// <summary>
/// The created-workspace event the procedures write to the outbox. They fill in workspaceId and
/// workspaceRefId with JSON_MODIFY, which adds a new key instead when the name differs, so the payload
/// must already carry both under exactly those names.
/// </summary>
public sealed class WorkspaceCreatedOutboxTests
{
    [Fact]
    public void The_payload_names_the_fields_the_procedures_fill_in()
    {
        var message = WorkspaceFakes.Created().For("lab");

        using var payload = JsonDocument.Parse(message.Payload);
        Assert.Equal(typeof(WorkspaceCreatedEvent).FullName, message.MessageType);
        Assert.Equal(0, payload.RootElement.GetProperty("workspaceId").GetInt32());
        Assert.Equal(Guid.Empty, payload.RootElement.GetProperty("workspaceRefId").GetGuid());
        Assert.Equal("lab", payload.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void The_signed_in_caller_is_named_as_the_actor()
    {
        var identity = new Mock<IIdentityService>();
        UserIdentity? caller = new UserIdentity(7) { UserRefId = Guid.NewGuid() };
        identity.Setup(i => i.TryGetUserIdentity(out caller)).Returns(true);

        var message = new WorkspaceCreatedOutbox(identity.Object).For("lab");

        using var payload = JsonDocument.Parse(message.Payload);
        Assert.Equal(caller!.UserRefId, payload.RootElement.GetProperty("actorUserRefId").GetGuid());
    }
}

/// <summary>
/// dbo.WorkspaceOwner_Grant builds the ownership event's JSON itself, for a workspace a member left
/// behind. This is the shape it writes; the outbox must read it back as the event.
/// </summary>
public sealed class WorkspaceHandOverPayloadTests
{
    [Fact]
    public void The_hand_over_written_by_the_database_reads_back_as_the_event()
    {
        const string payload = """
            {"workspaceId":12,"newOwnerRefId":"6F9619FF-8B86-D011-B42D-00C04FC964FF","changes":[{"field":"owner","before":"0f8fad5b-d9cb-469f-a165-70867728950e","after":"6f9619ff-8b86-d011-b42d-00c04fc964ff"}],"createdAtUtc":"2026-11-19T10:00:00.123Z"}
            """;

        var handedOver = JsonSerializer.Deserialize<WorkspaceOwnershipTransferredEvent>(payload, Wbskt.Infrastructure.Events.Outbox.OutboxMessage.JsonOptions)!;

        Assert.Equal(12, handedOver.WorkspaceId);
        Assert.Equal(Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"), handedOver.NewOwnerRefId);
        Assert.Equal(new FieldChange("owner", "0f8fad5b-d9cb-469f-a165-70867728950e", "6f9619ff-8b86-d011-b42d-00c04fc964ff"), Assert.Single(handedOver.Changes!));
        Assert.Equal(new DateTime(2026, 11, 19, 10, 0, 0, 123, DateTimeKind.Utc), handedOver.CreatedAtUtc);
    }
}
