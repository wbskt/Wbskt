using System.Text.Json;
using Moq;
using Wbskt.Auth.Host.Services;
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
