using Moq;
using Wbskt.Host.Tests.Abstraction;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Protocol;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>A device a step sends to must be one of the workspace's; one that is not registered is a warning.</summary>
public sealed class DeviceReferenceCheckTests
{
    private const int WorkspaceId = 1;
    private static readonly Guid ClientRef = Guid.NewGuid();

    private readonly Mock<IClientProvider> _clients = new();

    [Fact]
    public async Task A_device_the_workspace_does_not_have_is_an_error_on_the_client_field()
    {
        var issue = Assert.Single(await CheckAsync());

        Assert.Equal("CLIENT_NOT_FOUND", issue.Code);
        Assert.Equal(ValidationSeverity.Error, issue.Severity);
        Assert.Equal("config.clientRef", issue.Field);
    }

    [Fact]
    public async Task Another_workspaces_device_reads_as_missing()
    {
        Device(WorkspaceId + 1, ClientStatus.Registered);

        Assert.Equal("CLIENT_NOT_FOUND", Assert.Single(await CheckAsync()).Code);
    }

    [Fact]
    public async Task A_revoked_device_is_a_warning()
    {
        Device(WorkspaceId, ClientStatus.Revoked);

        var issue = Assert.Single(await CheckAsync());

        Assert.Equal("CLIENT_NOT_REGISTERED", issue.Code);
        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
    }

    [Fact]
    public async Task A_registered_device_of_the_workspace_is_fine()
    {
        Device(WorkspaceId, ClientStatus.Registered);

        Assert.Empty(await CheckAsync());
    }

    private void Device(int workspaceId, ClientStatus status) =>
        _clients.Setup(c => c.FindDetailByRefIdAsync(ClientRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientDetail { RefId = ClientRef, WorkspaceId = workspaceId, Name = "gh-heater", Status = status });

    private Task<IReadOnlyList<Wbskt.Workflow.Abstraction.Validation.ValidationIssue>> CheckAsync()
    {
        var send = new SendClientMessageNode
        {
            NodeId = Guid.NewGuid(),
            Name = "Heater on",
            Ports = [],
            Config = new SendClientMessageConfig { ClientRef = ClientRef.ToString(), Type = "heater.on" }
        };
        WorkflowDefinition def = ValidWorkflowBuilder.Build();
        return new DeviceReferenceCheck(_clients.Object).CheckAsync(WorkspaceId, def with { Nodes = [.. def.Nodes, send] }, CancellationToken.None);
    }
}
