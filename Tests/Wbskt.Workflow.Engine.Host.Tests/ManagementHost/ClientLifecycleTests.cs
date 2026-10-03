using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
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
        foreignResult.Error.Code.Should().Be("CLIENT_UNAUTHORIZED");
        harness.Provider.Verify(p => p.DeleteAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.Bus.Verify(b => b.PublishAsync(It.IsAny<ClientDeletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
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
        harness.Bus.Verify(b => b.PublishAsync(
            It.Is<ClientSecretRotatedEvent>(e => e.ClientRefId == client.RefId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_batch_reports_each_failure_and_still_applies_the_rest()
    {
        var harness = new Harness();
        var first = harness.AddClient(status: ClientStatus.Pending);
        var full = harness.AddClient(status: ClientStatus.Pending);
        var foreign = harness.AddClient(workspaceId: 99, status: ClientStatus.Pending);
        harness.Provider
            .Setup(p => p.UpdateStatusAsync(full.Id, ClientStatus.Registered, It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptionNumbered(50020));

        var result = await harness.Service.UpdateStatusesAsync(
            WorkspaceId, [first.RefId, full.RefId, foreign.RefId, first.RefId], ClientStatus.Registered);

        result.IsSuccess.Should().BeTrue();
        result.Value.Updated.Should().Equal(first.RefId);
        result.Value.Failed.Select(f => (f.ClientRefId, f.Code)).Should().Equal(
            (full.RefId, "POLICY_LIMIT_REACHED"),
            (foreign.RefId, "CLIENT_UNAUTHORIZED"));
        harness.Provider.Verify(p => p.UpdateStatusAsync(first.Id, ClientStatus.Registered, It.IsAny<CancellationToken>()), Times.Once);
        harness.Provider.Verify(p => p.UpdateStatusAsync(foreign.Id, It.IsAny<ClientStatus>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // SqlException has no public constructor; this builds one carrying just an error number.
    private static SqlException SqlExceptionNumbered(int number)
    {
        var error = (SqlError)RuntimeHelpers.GetUninitializedObject(typeof(SqlError));
        typeof(SqlError).GetField("_number", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(error, number);
        var errors = (SqlErrorCollection)RuntimeHelpers.GetUninitializedObject(typeof(SqlErrorCollection));
        typeof(SqlErrorCollection).GetField("_errors", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(errors, new List<object> { error });
        var exception = (SqlException)RuntimeHelpers.GetUninitializedObject(typeof(SqlException));
        typeof(SqlException).GetField("_errors", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(exception, errors);
        return exception;
    }

    private sealed class Harness
    {
        private int _nextId = 40;

        public Harness()
        {
            Provider
                .Setup(p => p.GetDetailByRefIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new NotFoundException("Client not found."));
            Policies
                .Setup(p => p.GetByRefIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RegistrationPolicy { Id = 5, RefId = Guid.NewGuid(), WorkspaceId = WorkspaceId });
            Service = new ClientService(Provider.Object, Policies.Object, Bus.Object, NullLogger<ClientService>.Instance);
        }

        public Mock<IClientProvider> Provider { get; } = new();

        public Mock<IRegistrationPolicyProvider> Policies { get; } = new();

        public Mock<IEventBus> Bus { get; } = new();

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
            Provider.Setup(p => p.GetDetailByRefIdAsync(client.RefId, It.IsAny<CancellationToken>())).ReturnsAsync(client);
            Provider.Setup(p => p.GetByIdAsync(client.Id, It.IsAny<CancellationToken>())).ReturnsAsync(client);
            return client;
        }
    }
}
