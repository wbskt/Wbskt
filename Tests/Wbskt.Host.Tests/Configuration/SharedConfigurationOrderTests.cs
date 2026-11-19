using Microsoft.AspNetCore.Builder;
using Wbskt.Infrastructure.Configuration;

namespace Wbskt.Host.Tests.Configuration;

/// <summary>
/// The shared files sit beneath the builder's own sources: the host's appsettings, environment
/// variables and a --Key=value argument all win over them.
/// </summary>
public sealed class SharedConfigurationOrderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wbskt-config-" + Guid.NewGuid().ToString("N"));
    private readonly string _contentRoot;

    public SharedConfigurationOrderTests()
    {
        _contentRoot = Path.Combine(_root, "Hosts", "Probe");
        Directory.CreateDirectory(_contentRoot);
        Directory.CreateDirectory(Path.Combine(_root, "Config"));
        File.WriteAllText(Path.Combine(_root, "Config", "probe.json"), """{ "Probe": { "Shared": "shared", "Local": "shared", "App": "shared", "Cli": "shared" } }""");
        File.WriteAllText(Path.Combine(_contentRoot, "probe.json"), """{ "Probe": { "Local": "local", "App": "local", "Cli": "local" } }""");
        File.WriteAllText(Path.Combine(_contentRoot, "appsettings.json"), """{ "Probe": { "App": "appsettings", "Cli": "appsettings" } }""");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Each_source_wins_over_the_shared_files_in_order()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = ["--Probe:Cli=command-line"],
            ContentRootPath = _contentRoot
        });

        builder.AddSharedConfiguration("probe.json");

        Assert.Equal("shared", builder.Configuration["Probe:Shared"]);
        Assert.Equal("local", builder.Configuration["Probe:Local"]);
        Assert.Equal("appsettings", builder.Configuration["Probe:App"]);
        Assert.Equal("command-line", builder.Configuration["Probe:Cli"]);
    }
}
