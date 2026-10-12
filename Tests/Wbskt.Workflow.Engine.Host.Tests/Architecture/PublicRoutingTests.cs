using System.Reflection;
using System.Text.RegularExpressions;
using Wbskt.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.Tests.Architecture;

/// <summary>
/// Which host a public URL reaches is decided by the Traefik labels in
/// deploy/compose/docker-compose.yml, not by the hosts. These read the labels and check them against
/// the hosts' routes, so a route added to one host cannot be swallowed by another's router, and no
/// public router reaches <see cref="InternalRoutes.Prefix"/>.
/// </summary>
public sealed partial class PublicRoutingTests
{
    private const string AnyGuid = "3fa85f64-5717-4562-b3fc-2c963f66afa6";

    private static readonly Assembly AuthHost = typeof(global::Wbskt.Auth.Host.Program).Assembly;
    private static readonly Assembly ManagementHost = typeof(global::Wbskt.Management.Host.Program).Assembly;

    private static readonly IReadOnlyDictionary<string, Router> Routers = ReadRouters();

    [Fact]
    public void No_public_router_reaches_an_internal_route()
    {
        string internalPath = "/" + InternalRoutes.ResolveWorkspace;

        var reaching = Routers.Values
            .Where(r => r.Public && r.Service is "auth" or "management" or "devices")
            .Where(r => !r.Rule.Contains($"!PathPrefix(`/{InternalRoutes.Prefix}`)", StringComparison.Ordinal) && r.Admits(internalPath))
            .Select(r => r.Name);

        Assert.Empty(reaching);
    }

    [Fact]
    public void The_api_host_sends_each_auth_administration_route_to_auth()
    {
        // Sign-in, refresh and sessions stay on auth.${DOMAIN}, where the refresh cookie lives.
        var missed = HostArchitectureTests.ActionsIn(AuthHost)
            .Where(a => !a.Route.StartsWith("api/auth", StringComparison.Ordinal) && !a.Route.StartsWith(InternalRoutes.Prefix, StringComparison.Ordinal))
            .Where(a => !Routers["auth-api"].Admits(Concrete(a.Route)))
            .Select(a => $"{a.Name} {a.Route}");

        Assert.Empty(missed);
    }

    [Fact]
    public void The_auth_router_on_the_api_host_takes_no_management_route()
    {
        var taken = HostArchitectureTests.ActionsIn(ManagementHost)
            .Where(a => Routers["auth-api"].Admits(Concrete(a.Route)))
            .Select(a => $"{a.Name} {a.Route}");

        Assert.Empty(taken);
    }

    [Fact]
    public void The_auth_router_on_the_api_host_outranks_management_and_yields_to_devices()
    {
        // Without an explicit priority Traefik ranks by rule length, which says nothing about intent.
        Assert.True(Routers["auth-api"].Priority > Routers["management"].Priority);
        Assert.True(Routers["auth-api"].Priority < Routers["devices"].Priority);
    }

    /// <summary>A route template as a request path: route parameters become a GUID.</summary>
    private static string Concrete(string route) => "/" + RouteParameter().Replace(route, AnyGuid);

    private sealed record Router(string Name, string Service, string Rule, bool Public, int Priority)
    {
        /// <summary>
        /// Whether the rule's path constraints let <paramref name="path"/> through. Only the positive
        /// PathPrefix and PathRegexp matchers are read, OR'd together; a rule without any admits every
        /// path. Negations are checked by the tests that care about them.
        /// </summary>
        public bool Admits(string path)
        {
            var prefixes = PositivePathPrefix().Matches(Rule).Select(m => m.Groups["value"].Value).ToList();
            var regexes = PathRegexp().Matches(Rule).Select(m => m.Groups["value"].Value).ToList();
            if (prefixes.Count == 0 && regexes.Count == 0)
            {
                return true;
            }

            return prefixes.Any(p => path.StartsWith(p, StringComparison.Ordinal)) || regexes.Any(r => Regex.IsMatch(path, r));
        }
    }

    private static IReadOnlyDictionary<string, Router> ReadRouters()
    {
        var labels = new Dictionary<string, Dictionary<string, string>>();
        var serviceOf = new Dictionary<string, string>();
        string? service = null;

        foreach (string line in File.ReadLines(ComposeFile()))
        {
            if (ServiceKey().Match(line) is { Success: true } key)
            {
                service = key.Groups["name"].Value;
                continue;
            }

            if (service is null || RouterLabel().Match(line) is not { Success: true } label)
            {
                continue;
            }

            string router = label.Groups["router"].Value;
            serviceOf.TryAdd(router, service);
            if (!labels.TryGetValue(router, out var values))
            {
                labels[router] = values = new Dictionary<string, string>();
            }

            // $$ is compose's escape for a literal $.
            values[label.Groups["key"].Value] = label.Groups["value"].Value.Trim().Replace("$$", "$");
        }

        return labels.ToDictionary(
            pair => pair.Key,
            pair => new Router(
                pair.Key,
                pair.Value.GetValueOrDefault("service", serviceOf[pair.Key]),
                pair.Value.GetValueOrDefault("rule", string.Empty),
                pair.Value.GetValueOrDefault("entrypoints") == "websecure",
                int.TryParse(pair.Value.GetValueOrDefault("priority"), out int priority) ? priority : pair.Value.GetValueOrDefault("rule", string.Empty).Length));
    }

    private static string ComposeFile()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "deploy", "compose", "docker-compose.yml");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("deploy/compose/docker-compose.yml was not found above the test output.");
    }

    [GeneratedRegex(@"^  (?<name>[a-z][a-z0-9-]*):\s*$")]
    private static partial Regex ServiceKey();

    [GeneratedRegex(@"^\s*-\s*traefik\.http\.routers\.(?<router>[a-z0-9-]+)\.(?<key>rule|entrypoints|priority|service)=(?<value>.*)$")]
    private static partial Regex RouterLabel();

    [GeneratedRegex(@"(?<!!)PathPrefix\(`(?<value>[^`]*)`\)")]
    private static partial Regex PositivePathPrefix();

    [GeneratedRegex(@"PathRegexp\(`(?<value>[^`]*)`\)")]
    private static partial Regex PathRegexp();

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex RouteParameter();
}
