using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Infrastructure.Security;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.EventBus.Abstractions;
using Wbskt.Primitives.Models;

namespace Wbskt.Workflow.Engine.Host.Tests.AuthHost;

/// <summary>
/// Issuing an invitation now mails the invitee. It also still returns the raw token, because an
/// administrator delivering the link by hand is how every invitation worked before this host could
/// send mail, and a relay outage must not take that away.
/// </summary>
public sealed class InvitationMailTests
{
    private static readonly Guid TenantRef = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task Issuing_an_invitation_mails_the_invitee()
    {
        var harness = new Harness();

        var result = await harness.Service.CreateInvitationAsync(
            callerId: 1, TenantRef, new CreateInvitationRequest("invitee@example.test", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["invite:invitee@example.test|Acme Ltd"], harness.Mailer.Sent);
    }

    /// <summary>
    /// The mail names the tenant the invitee is being asked to join, and that name comes back from
    /// the procedure that created the invitation rather than a second lookup.
    /// </summary>
    [Fact]
    public async Task The_mail_names_the_tenant_the_procedure_reported()
    {
        var harness = new Harness(tenantName: "Northwind Robotics");

        await harness.Service.CreateInvitationAsync(
            callerId: 1, TenantRef, new CreateInvitationRequest("invitee@example.test", null), CancellationToken.None);

        Assert.Equal(["invite:invitee@example.test|Northwind Robotics"], harness.Mailer.Sent);
    }

    [Fact]
    public async Task The_raw_token_is_still_returned_once_for_hand_delivery()
    {
        var harness = new Harness();

        var result = await harness.Service.CreateInvitationAsync(
            callerId: 1, TenantRef, new CreateInvitationRequest("invitee@example.test", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.Token));

        // What is stored is the hash of exactly that string. A token returned to the caller that did
        // not hash to the stored value would be unredeemable, and nothing else would notice.
        harness.Provider.Verify(
            p => p.CreateInvitationAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(),
                It.Is<byte[]>(h => h.SequenceEqual(SecurityTokens.Hash(result.Value.Token))),
                It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// A caller without <c>users.manage</c> gets nothing — and, in particular, no mail is sent to an
    /// address they chose. An unauthorised invitation that still mailed someone would make this
    /// endpoint a way to send mail through the platform.
    /// </summary>
    [Fact]
    public async Task An_unauthorised_caller_causes_no_mail()
    {
        var harness = new Harness(permitted: false);

        var result = await harness.Service.CreateInvitationAsync(
            callerId: 1, TenantRef, new CreateInvitationRequest("invitee@example.test", null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(harness.Mailer.Sent);
        harness.Provider.Verify(
            p => p.CreateInvitationAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<byte[]>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private sealed class Harness
    {
        public Harness(string tenantName = "Acme Ltd", bool permitted = true)
        {
            Provider.Setup(p => p.FindTenantIdByRefIdForUserAsync(TenantRef, 1, It.IsAny<CancellationToken>())).ReturnsAsync(5);
            Provider.Setup(p => p.VerifyPermissionAsync(1, 5, null, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(permitted);
            Provider.Setup(p => p.CreateInvitationAsync(
                    It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<byte[]>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreatedInvitation(Guid.NewGuid(), tenantName));

            Service = new ManagementService(
                Provider.Object,
                Mock.Of<IWorkspaceProvider>(),
                Mock.Of<IEventBus>(),
                Mailer,
                NullLogger<ManagementService>.Instance,
                Mock.Of<IAccessTokenRevocation>());
        }

        public Mock<IAuthProvider> Provider { get; } = new();

        public RecordingMailer Mailer { get; } = new();

        public ManagementService Service { get; }
    }

    /// <summary>Records recipient and tenant name; the token is deliberately not captured.</summary>
    private sealed class RecordingMailer : IAuthMailer
    {
        public List<string> Sent { get; } = [];

        public bool IsConfigured => true;

        public ValueTask QueueInvitationAsync(string to, string tenantName, string token, DateTime expiresAtUtc, CancellationToken ct)
        {
            Sent.Add($"invite:{to}|{tenantName}");
            return ValueTask.CompletedTask;
        }

        public ValueTask QueueEmailVerificationAsync(string to, string username, string token, DateTime expiresAtUtc, CancellationToken ct) => ValueTask.CompletedTask;

        public ValueTask QueuePasswordResetAsync(string to, string username, string token, DateTime expiresAtUtc, CancellationToken ct) => ValueTask.CompletedTask;

        public ValueTask QueueAccountAlreadyExistsAsync(string to, string username, CancellationToken ct) => ValueTask.CompletedTask;
    }
}
