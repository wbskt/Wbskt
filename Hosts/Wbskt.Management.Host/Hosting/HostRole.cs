using System.Reflection;
using Microsoft.AspNetCore.Mvc.Controllers;
using Wbskt.Management.Host.Controllers;

namespace Wbskt.Management.Host.Hosting;

/// <summary>
/// Which part of the API an instance of this host serves, from <c>Host:Role</c>.
/// <list type="bullet">
/// <item><see cref="All"/> (the default): the console API and the client endpoints.</item>
/// <item><see cref="ClientAuth"/>: only client registration and login. Deployed as its own container
/// (<c>client-auth</c> in deploy/compose) that Traefik sends those paths to, so a console deploy or a
/// heavy console query does not stop clients signing in. The <c>All</c> instances keep serving the
/// same paths, and Traefik falls back to them while no client-auth instance is healthy.</item>
/// </list>
/// </summary>
public enum HostRole
{
    All,
    ClientAuth
}

public static class HostRoles
{
    public const string ConfigurationKey = "Host:Role";

    // The role's name before the Device wording went (wbskt/Wbskt#574), still read so a host left
    // configured with it starts as the role it meant.
    private const string LegacyClientAuthName = "Devices";

    /// <summary>The controllers a <see cref="HostRole.ClientAuth"/> instance serves: the anonymous client edge.</summary>
    public static readonly IReadOnlySet<Type> ClientAuthControllers = new HashSet<Type>
    {
        typeof(ClientAuthController),
        typeof(ClientRegistrationsController)
    };

    /// <summary>
    /// The configured role; <see cref="HostRole.All"/> when unset. Anything else fails startup
    /// rather than quietly serving the whole API from a container meant to serve only clients.
    /// </summary>
    public static HostRole FromConfiguration(IConfiguration configuration)
    {
        var value = configuration[ConfigurationKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            return HostRole.All;
        }

        if (string.Equals(value.Trim(), LegacyClientAuthName, StringComparison.OrdinalIgnoreCase))
        {
            return HostRole.ClientAuth;
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
        role == HostRole.All || ClientAuthControllers.Contains(controller);
}

/// <summary>Discovers only the controllers the instance's <see cref="HostRole"/> serves.</summary>
internal sealed class HostRoleControllerFeatureProvider(HostRole role) : ControllerFeatureProvider
{
    protected override bool IsController(TypeInfo typeInfo) =>
        base.IsController(typeInfo) && role.Serves(typeInfo.AsType());
}
