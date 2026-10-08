using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

/// <summary>
/// Deleting a client, rotating its secret, and changing many clients' status at once. What matters
/// is that another workspace's client is untouched and indistinguishable from a missing one, that a
/// rotated secret is returned once and only its hash is stored, and that one failure in a batch
/// does not stop the rest.
/// </summary>
public sealed class ClientLifecycleTests
{
    private const int WorkspaceId = 7;

    [Fact]
    public async Task Deleting_a_client_removes_it_and_announces_it()
    {
        var harness = new Harness();
        var client = harness.AddClient();
        harness.Provider.Setup(p => p.DeleteAsync(client.Id, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await harness.Service.DeleteAsync(WorkspaceId, client.RefId);

        result.IsSuccess.Should().BeTrue();
        harness.Cutoffs.Verify(c => c.RevokeAsync(client.RefId), Times.Once);
        harness.Bus.Verify(b => b.PublishAsync(
            It.Is<ClientDeletedEvent>(e => e.ClientRefId == client.RefId && e.WorkspaceId == WorkspaceId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Another_workspaces_client_reads_like_a_missing_one_and_is_not_deleted()
    {
        var harness = new Harness();
        var foreign = harness.AddClient(workspaceId: 99);

        var foreignResult = await harness.Service.DeleteAsync(WorkspaceId, foreign.RefId);
        var missingResult = await harness.Service.DeleteAsync(WorkspaceId, Guid.NewGuid());

        foreignResult.Error.Should().Be(missingResult.Error);
        foreignResult.Error.Code.Should().Be("CLIENT_NOT_FOUND");
        harness.Provider.Verify(p => p.DeleteAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.Bus.Verify(b => b.PublishAsync(It.IsAny<ClientDeletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.Cutoffs.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Rotating_a_secret_stores_only_its_hash_and_returns_it_once()
    {
        var harness = new Harness();
        var client = harness.AddClient();
        byte[]? storedHash = null;
        harness.Provider
            .Setup(p => p.UpdateSecretAsync(client.Id, WorkspaceId, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, byte[], CancellationToken>((_, _, hash, _) => storedHash = hash)
            .ReturnsAsync(true);

        var result = await harness.Service.RotateSecretAsync(WorkspaceId, client.RefId);

        result.IsSuccess.Should().BeTrue();
        result.Value.ClientRefId.Should().Be(client.RefId);
        ClientSecrets.Matches(storedHash, result.Value.Secret).Should().BeTrue();
        // The cutoff and the event carry the same moment, so the socket host's list and Redis agree.
        harness.Cutoffs.Verify(c => c.RevokeIssuedBeforeAsync(client.RefId, It.IsAny<DateTime>()), Times.Once);
        var cutoff = (DateTime)harness.Cutoffs.Invocations.Single(i => i.Method.Name == nameof(IClientTokenCutoffs.RevokeIssuedBeforeAsync)).Arguments[1];
        harness.Bus.Verify(b => b.PublishAsync(
            It.Is<ClientSecretRotatedEvent>(e => e.ClientRefId == client.RefId && e.RotatedAt == cutoff),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Revoking_a_device_records_a_cutoff_for_every_token_it_holds()
    {
        var harness = new Harness();
        var client = harness.AddClient(status: ClientStatus.Registered);

        var result = await harness.Service.UpdateStatusAsync(WorkspaceId, client.RefId, ClientStatus.Revoked);

        result.IsSuccess.Should().BeTrue();
        harness.Cutoffs.Verify(c => c.RevokeAsync(client.RefId), Times.Once);
        harness.Cutoffs.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Approving_a_device_again_lets_it_back_in_with_a_fresh_token_only()
    {
        var harness = new Harness();
        var client = harness.AddClient(status: ClientStatus.Revoked);
        var before = DateTime.UtcNow;

        var result = await harness.Service.UpdateStatusAsync(WorkspaceId, client.RefId, ClientStatus.Registered);

        result.IsSuccess.Should().BeTrue();
        harness.Cutoffs.Verify(c => c.ReinstateAsync(client.RefId, It.Is<DateTime>(t => t >= before)), Times.Once);
        harness.Cutoffs.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Rejecting_a_pending_device_records_nothing_because_it_holds_no_token()
    {
        var harness = new Harness();
        var client = harness.AddClient(status: ClientStatus.Pending);

        var result = await harness.Service.UpdateStatusAsync(WorkspaceId, client.RefId, ClientStatus.Rejected);

        result.IsSuccess.Should().BeTrue();
        harness.Cutoffs.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_batch_reports_each_failure_and_still_applies_the_rest()
    {
        var harness = new Harness();
        var first = harness.AddClient(status: ClientStatus.Pending);
        var full = harness.AddClient(status: ClientStatus.Pending);
        var foreign = harness.AddClient(workspaceId: 99, status: ClientStatus.Pending);
        harness.Full.Add(full.RefId);

        var result = await harness.Service.UpdateStatusesAsync(
            WorkspaceId, [first.RefId, full.RefId, foreign.RefId, first.RefId], ClientStatus.Registered);

        result.IsSuccess.Should().BeTrue();
        result.Value.Updated.Should().Equal(first.RefId);
        result.Value.Failed.Select(f => (f.ClientRefId, f.Code)).Should().Equal(
            (full.RefId, "POLICY_LIMIT_REACHED"),
            (foreign.RefId, "CLIENT_NOT_FOUND"));
        harness.Bus.Verify(b => b.PublishAsync(It.Is<ClientStatusChangedEvent>(e => e.ClientRefId == first.RefId), It.IsAny<CancellationToken>()), Times.Once);
        harness.Bus.Verify(b => b.PublishAsync(It.IsAny<ClientStatusChangedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_batch_is_one_database_call_with_each_client_once_in_the_order_given()
    {
        var harness = new Harness();
        var a = harness.AddClient(status: ClientStatus.Pending);
        var b = harness.AddClient(status: ClientStatus.Pending);

        await harness.Service.UpdateStatusesAsync(WorkspaceId, [b.RefId, a.RefId, b.RefId], ClientStatus.Registered);

        harness.Provider.Verify(p => p.UpdateStatusesAsync(
            WorkspaceId,
            It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { b.RefId, a.RefId })),
            ClientStatus.Registered,
            It.IsAny<CancellationToken>()), Times.Once);
        harness.Provider.Verify(p => p.FindDetailByRefIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task One_client_reads_another_workspaces_client_like_a_missing_one()
    {
        var harness = new Harness();
        var foreign = harness.AddClient(workspaceId: 99);

        var missing = await harness.Service.UpdateStatusAsync(WorkspaceId, Guid.NewGuid(), ClientStatus.Revoked);
        var other = await harness.Service.UpdateStatusAsync(WorkspaceId, foreign.RefId, ClientStatus.Revoked);

        missing.Error.Code.Should().Be("CLIENT_NOT_FOUND");
        other.Error.Should().Be(missing.Error);
        harness.Bus.Verify(b => b.PublishAsync(It.IsAny<ClientStatusChangedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_client_already_at_the_status_changes_nothing_and_announces_nothing()
    {
        var harness = new Harness();
        var client = harness.AddClient(status: ClientStatus.Registered);

        var result = await harness.Service.UpdateStatusAsync(WorkspaceId, client.RefId, ClientStatus.Registered);

        result.IsSuccess.Should().BeTrue();
        harness.Cutoffs.VerifyNoOtherCalls();
        harness.Bus.Verify(b => b.PublishAsync(It.IsAny<ClientStatusChangedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class Harness
    {
        private int _nextId = 40;

        public Harness()
        {
            Provider
                .Setup(p => p.FindDetailByRefIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ClientDetail?)null);
            // A stand-in for dbo.Client_UpdateStatuses, answering from the clients added below.
            Provider
                .Setup(p => p.UpdateStatusesAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<ClientStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((int workspaceId, IReadOnlyList<Guid> refIds, ClientStatus status, CancellationToken _) =>
                    refIds.Select(refId => Change(workspaceId, refId, status)).ToList());
            Service = new ClientService(Provider.Object, Bus.Object, Cutoffs.Object, NullLogger<ClientService>.Instance);
        }

        public Mock<IClientProvider> Provider { get; } = new();

        /// <summary>Clients whose policy has no places left.</summary>
        public HashSet<Guid> Full { get; } = [];

        private Dictionary<Guid, ClientDetail> Clients { get; } = [];

        public Mock<IEventBus> Bus { get; } = new();

        public Mock<IClientTokenCutoffs> Cutoffs { get; } = new();

        public ClientService Service { get; }

        public ClientDetail AddClient(int workspaceId = WorkspaceId, ClientStatus status = ClientStatus.Registered)
        {
            var client = new ClientDetail
            {
                Id = ++_nextId,
                RefId = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                PolicyId = 5,
                PolicyRefId = Guid.NewGuid(),
                Name = $"device-{_nextId}",
                Status = status
            };
            Provider.Setup(p => p.FindDetailByRefIdAsync(client.RefId, It.IsAny<CancellationToken>())).ReturnsAsync(client);
            Clients[client.RefId] = client;
            return client;
        }

        private ClientStatusChange Change(int workspaceId, Guid refId, ClientStatus status)
        {
            if (!Clients.TryGetValue(refId, out var client))
            {
                return new ClientStatusChange(refId, 0, 0, Guid.Empty, default, ClientStatusOutcome.NotFound);
            }

            var outcome = client.WorkspaceId != workspaceId ? ClientStatusOutcome.OtherWorkspace
                : client.Status == status ? ClientStatusOutcome.AlreadyAtStatus
                : status == ClientStatus.Registered && Full.Contains(refId) ? ClientStatusOutcome.PolicyLimitReached
                : ClientStatusOutcome.Updated;
            return new ClientStatusChange(refId, client.Id, client.PolicyId, client.PolicyRefId, client.Status, outcome);
        }
    }
}
