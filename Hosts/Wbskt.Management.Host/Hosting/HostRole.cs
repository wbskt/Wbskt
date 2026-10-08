using System.Reflection;
using Microsoft.AspNetCore.Mvc.Controllers;
using Wbskt.Management.Host.Controllers;

namespace Wbskt.Management.Host.Hosting;

/// <summary>
/// Which part of the API an instance of this host serves, from <c>Host:Role</c>.
/// <list type="bullet">
/// <item><see cref="All"/> (the default): the console API and the device endpoints.</item>
/// <item><see cref="Devices"/>: only device registration and login. Deployed as its own container
/// (<c>devices</c> in deploy/compose) that Traefik sends those paths to, so a console deploy or a
/// heavy console query does not stop devices signing in. The <c>All</c> instances keep serving the
/// same paths, and Traefik falls back to them while no devices instance is healthy.</item>
/// </list>
/// </summary>
public enum HostRole
{
    All,
    Devices
}

public static class HostRoles
{
    public const string ConfigurationKey = "Host:Role";

    /// <summary>The controllers a <see cref="HostRole.Devices"/> instance serves: the anonymous device edge.</summary>
    public static readonly IReadOnlySet<Type> DeviceControllers = new HashSet<Type>
    {
        typeof(ClientAuthController),
        typeof(ClientRegistrationsController)
    };

    /// <summary>
    /// The configured role; <see cref="HostRole.All"/> when unset. Anything else fails startup
    /// rather than quietly serving the whole API from a container meant to serve only devices.
    /// </summary>
    public static HostRole FromConfiguration(IConfiguration configuration)
    {
        var value = configuration[ConfigurationKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            return HostRole.All;
        }

        // By name only: Enum.TryParse would also take "1".
        var name = Enum.GetNames<HostRole>().FirstOrDefault(n => string.Equals(n, value.Trim(), StringComparison.OrdinalIgnoreCase));
        if (name is not null)
        {
            return Enum.Parse<HostRole>(name);
        }

        throw new InvalidOperationException($"{ConfigurationKey} is '{value}'; expected one of: {string.Join(", ", Enum.GetNames<HostRole>())}.");
    }

    public static bool Serves(this HostRole role, Type controller) =>
        role == HostRole.All || DeviceControllers.Contains(controller);
}

/// <summary>Discovers only the controllers the instance's <see cref="HostRole"/> serves.</summary>
internal sealed class HostRoleControllerFeatureProvider(HostRole role) : ControllerFeatureProvider
{
    protected override bool IsController(TypeInfo typeInfo) =>
        base.IsController(typeInfo) && role.Serves(typeInfo.AsType());
}
