using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Wbskt.Management.Host.Controllers;
using Wbskt.Management.Host.Hosting;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class HostRoleTests
{
    private static IConfiguration Config(string? role) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(role is null ? [] : [new KeyValuePair<string, string?>(HostRoles.ConfigurationKey, role)])
            .Build();

    [Theory]
    [InlineData(null, HostRole.All)]
    [InlineData("", HostRole.All)]
    [InlineData("All", HostRole.All)]
    [InlineData("Devices", HostRole.Devices)]
    [InlineData(" devices ", HostRole.Devices)]
    public void The_role_comes_from_configuration_and_defaults_to_everything(string? value, HostRole expected)
    {
        HostRoles.FromConfiguration(Config(value)).Should().Be(expected);
    }

    [Theory]
    [InlineData("Device")]
    [InlineData("1")]
    [InlineData("console")]
    public void An_unknown_role_fails_startup_instead_of_serving_everything(string value)
    {
        var act = () => HostRoles.FromConfiguration(Config(value));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{value}*");
    }

    [Fact]
    public void A_devices_instance_serves_only_device_registration_and_login()
    {
        var controllers = typeof(ClientsController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        controllers.Where(t => HostRole.Devices.Serves(t))
            .Should().BeEquivalentTo([typeof(ClientAuthController), typeof(ClientRegistrationsController)]);
        controllers.Should().OnlyContain(t => HostRole.All.Serves(t));
    }

    /// <summary>
    /// Everything a devices instance serves is reached before a user has signed in, so none of it
    /// may need a user token. A controller added to the set that does would fail on every call.
    /// </summary>
    [Fact]
    public void Every_controller_a_devices_instance_serves_is_anonymous()
    {
        HostRoles.DeviceControllers.Should().OnlyContain(t =>
            t.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), true).Length > 0);
    }
}
