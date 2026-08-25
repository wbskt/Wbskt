using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

/// <summary>
/// Wraps a <c>sqlpackage /Action:Publish</c> invocation to deploy one of the two DACPACs against a
/// freshly created integration-test database.
/// </summary>
public static class DatabaseDeployer
{
    /// <summary>The workflow/management database — tables, procedures and the engine's storage.</summary>
    public const string WorkflowProject = "Wbskt.Database";

    /// <summary>Users, tenants, roles, invitations and the account-recovery tokens.</summary>
    public const string AuthProject = "Wbskt.Database.Auth";

    /// <param name="projectName">
    /// Which DACPAC to publish — <see cref="WorkflowProject"/> or <see cref="AuthProject"/>. The two
    /// are separate databases in every environment, so a fixture deploys whichever one its tests
    /// address rather than both.
    /// </param>
    public static async Task DeployAsync(
        string masterConnectionString,
        string dbName,
        string dbConnectionString,
        string projectName = WorkflowProject)
    {
        await CreateDatabaseAsync(masterConnectionString, dbName);

        string dacpacPath = FindDacpacPath(projectName);
        string sqlPackageExe = FindSqlPackage();

        var args = string.Join(" ", [
            "/Action:Publish",
            $"/SourceFile:\"{dacpacPath}\"",
            $"/TargetConnectionString:\"{dbConnectionString}\"",
            "/p:BlockOnPossibleDataLoss=False"
        ]);

        var psi = new ProcessStartInfo(sqlPackageExe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start sqlpackage process.");

        string stdout = await process.StandardOutput.ReadToEndAsync();
        string stderr = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"sqlpackage exited with code {process.ExitCode}.\nstdout: {stdout}\nstderr: {stderr}");
        }
    }

    private static async Task CreateDatabaseAsync(string masterConnectionString, string dbName)
    {
        await using var conn = new SqlConnection(masterConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"IF DB_ID(N'{dbName}') IS NULL CREATE DATABASE [{dbName}];";
        await cmd.ExecuteNonQueryAsync();
    }

    private static string FindDacpacPath(string projectName)
    {
        string baseDir = AppContext.BaseDirectory;
        DirectoryInfo? dir = new DirectoryInfo(baseDir);

        // Walk up from the test output directory until we find the solution root (has Wbskt.slnx)
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Wbskt.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new FileNotFoundException(
                "Could not find solution root (Wbskt.slnx) while searching for DACPAC.");
        }

        string binRoot = Path.Combine(dir.FullName, "Databases", projectName, "bin");

        // Searched recursively rather than by naming the configuration folder. MSBuild.Sdk.SqlProj
        // emits to bin/<Config>/<TFM>/, so the previous bin/<Config>/ candidates matched nothing —
        // the deploy threw, the fixture set IsAvailable = false, and all 36+ tests reported as
        // Skipped. That reads as "no SQL Server reachable" and hid the real cause completely.
        string? found = FindDacpacUnder(binRoot, projectName);
        if (found is not null)
        {
            return found;
        }

        // Not built yet: build it, then look again.
        BuildDacpac(dir.FullName, projectName);

        return FindDacpacUnder(binRoot, projectName)
               ?? throw new FileNotFoundException(
                   $"{projectName}.dacpac not found anywhere under '{binRoot}', including after a build. " +
                   $"Run: dotnet build Databases/{projectName}/{projectName}.sqlproj");
    }

    /// <summary>
    /// The most recently written <c>&lt;projectName&gt;.dacpac</c> anywhere under
    /// <paramref name="binRoot"/>, or <c>null</c> if there is none. Recursive so that a change to the
    /// SDK's output layout cannot silently disable the whole integration suite again.
    /// </summary>
    private static string? FindDacpacUnder(string binRoot, string projectName)
    {
        if (!Directory.Exists(binRoot))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(binRoot, $"{projectName}.dacpac", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static void BuildDacpac(string solutionRoot, string projectName)
    {
        string sqlprojPath = Path.Combine(
            solutionRoot, "Databases", projectName, $"{projectName}.sqlproj");

        var psi = new ProcessStartInfo("dotnet", $"build \"{sqlprojPath}\" -c Debug")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        process?.WaitForExit(60_000);
    }

    private static string FindSqlPackage()
    {
        // An explicit path wins. CI installs the tool to a known location and should not depend on
        // probing succeeding.
        string? configured = Environment.GetEnvironmentVariable("WBSKT_SQLPACKAGE_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        // PATH lookup. The lookup command is itself platform-specific: `where` exists only on
        // Windows, so probing with it unconditionally made this method Windows-only — on Linux both
        // attempts threw, and the .exe-suffixed fallback below could never match either.
        string locator = OperatingSystem.IsWindows() ? "where" : "which";

        foreach (string name in new[] { "sqlpackage", "sqlpackage.exe" })
        {
            try
            {
                var lookup = new ProcessStartInfo(locator, name)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(lookup);
                string result = proc?.StandardOutput.ReadLine() ?? string.Empty;
                proc?.WaitForExit();

                if (!string.IsNullOrWhiteSpace(result) && File.Exists(result.Trim()))
                {
                    return result.Trim();
                }
            }
            catch
            {
                // Locator absent or not executable here — fall through to the well-known paths.
            }
        }

        // Where `dotnet tool install -g microsoft.sqlpackage` puts it. The Windows build carries the
        // .exe suffix; the Unix build does not.
        string toolsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools");

        foreach (string candidate in new[]
                 {
                     Path.Combine(toolsDir, "sqlpackage"),
                     Path.Combine(toolsDir, "sqlpackage.exe")
                 })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            "sqlpackage not found. Install via: dotnet tool install -g microsoft.sqlpackage, " +
            "or set WBSKT_SQLPACKAGE_PATH to its full path.");
    }
}
