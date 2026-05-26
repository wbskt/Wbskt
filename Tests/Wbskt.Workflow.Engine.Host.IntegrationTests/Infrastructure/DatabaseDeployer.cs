using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

/// <summary>
/// Wraps a <c>sqlpackage /Action:Publish</c> invocation to deploy the workflow DACPAC
/// against a freshly created integration-test database.
/// </summary>
public static class DatabaseDeployer
{
    public static async Task DeployAsync(
        string masterConnectionString,
        string dbName,
        string dbConnectionString)
    {
        await CreateDatabaseAsync(masterConnectionString, dbName);

        string dacpacPath = FindDacpacPath();
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

    private static string FindDacpacPath()
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

        // Try Debug first, then Release
        string[] candidates =
        [
            Path.Combine(dir.FullName, "Databases", "Wbskt.Database", "bin", "Debug", "Wbskt.Database.dacpac"),
            Path.Combine(dir.FullName, "Databases", "Wbskt.Database", "bin", "Release", "Wbskt.Database.dacpac"),
        ];

        foreach (string path in candidates)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        // Attempt to build it on the fly
        BuildDacpac(dir.FullName);

        foreach (string path in candidates)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException(
            $"Wbskt.Database.dacpac not found. Tried: {string.Join(", ", candidates)}. " +
            "Run: dotnet build Databases/Wbskt.Database/Wbskt.Database.sqlproj");
    }

    private static void BuildDacpac(string solutionRoot)
    {
        string sqlprojPath = Path.Combine(
            solutionRoot, "Databases", "Wbskt.Database", "Wbskt.Database.sqlproj");

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
        // Check system PATH first
        foreach (string name in new[] { "sqlpackage", "sqlpackage.exe" })
        {
            try
            {
                var which = new ProcessStartInfo("where", name)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(which);
                string result = proc?.StandardOutput.ReadLine() ?? string.Empty;
                proc?.WaitForExit();
                if (!string.IsNullOrWhiteSpace(result) && File.Exists(result.Trim()))
                {
                    return result.Trim();
                }
            }
            catch { /* ignore */ }
        }

        // Windows dotnet tools path
        string dotnetToolsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".dotnet", "tools", "sqlpackage.exe");

        if (File.Exists(dotnetToolsPath))
        {
            return dotnetToolsPath;
        }

        throw new FileNotFoundException(
            "sqlpackage not found. Install via: dotnet tool install -g microsoft.sqlpackage");
    }
}
