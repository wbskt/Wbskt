using Microsoft.AspNetCore.Routing;
using Wbskt.Auth.Host.Controllers;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Host.Tests.Infrastructure;

/// <summary>
/// An access change names whom it reaches, so the management host re-checks only those live feeds
/// (#1001); anything it can't read, including the "1" an older auth host sends, reaches everyone.
/// </summary>
public sealed class AccessChangeTests
{
    private static readonly Guid Ada = Guid.NewGuid();
    private static readonly Guid Workspace = Guid.NewGuid();

    [Fact]
    public void A_scope_survives_the_channel()
    {
        var sent = AccessChange.ForUsers(Ada).Union(AccessChange.ForWorkspaces(Workspace));

        var read = AccessChange.Parse(sent.ToMessage());

        Assert.False(read.Everyone);
        Assert.Equal([Ada], read.Users);
        Assert.Equal([Workspace], read.Workspaces);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("{not json")]
    [InlineData("{}")]
    public void A_message_without_a_scope_reaches_everyone(string? message)
    {
        Assert.True(AccessChange.Parse(message).Everyone);
    }

    [Fact]
    public void Everyone_is_announced_as_the_message_older_hosts_send()
    {
        Assert.Equal("1", AccessChange.All.ToMessage());
    }

    [Fact]
    public void A_union_with_everyone_is_everyone()
    {
        Assert.True(AccessChange.ForUsers(Ada).Union(AccessChange.All).Everyone);
    }

    [Fact]
    public void A_user_change_reaches_that_user_and_a_connection_whose_user_is_unknown()
    {
        var change = AccessChange.ForUsers(Ada);

        Assert.True(change.Touches(Ada, null, []));
        Assert.True(change.Touches(null, null, []));
        Assert.False(change.Touches(Guid.NewGuid(), null, [Workspace]));
    }

    [Fact]
    public void A_workspace_change_reaches_whoever_reads_that_workspace()
    {
        var change = AccessChange.ForWorkspaces(Workspace);

        Assert.True(change.Touches(Guid.NewGuid(), null, [Guid.NewGuid(), Workspace]));
        Assert.False(change.Touches(null, null, [Guid.NewGuid()]));
    }

    [Fact]
    public void The_auth_host_scopes_an_announcement_to_the_member_or_workspace_in_the_route()
    {
        var member = AnnouncesWorkspaceAccessChangeAttribute.ScopeOf(new RouteValueDictionary
        {
            ["tenantRef"] = Guid.NewGuid().ToString(),
            ["userRef"] = Ada.ToString(),
            ["groupRef"] = Guid.NewGuid().ToString()
        });
        var workspace = AnnouncesWorkspaceAccessChangeAttribute.ScopeOf(new RouteValueDictionary { ["workspaceRef"] = Workspace.ToString() });
        var role = AnnouncesWorkspaceAccessChangeAttribute.ScopeOf(new RouteValueDictionary
        {
            ["tenantRef"] = Guid.NewGuid().ToString(),
            ["roleRef"] = Guid.NewGuid().ToString()
        });

        Assert.Equal([Ada], member.Users);
        Assert.Equal([Workspace], workspace.Workspaces);
        Assert.True(role.Everyone);
    }
}
