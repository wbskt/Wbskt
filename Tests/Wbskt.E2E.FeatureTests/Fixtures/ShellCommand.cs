using System.Diagnostics;

namespace Wbskt.E2E.FeatureTests.Fixtures;

/// <summary>
/// Runs a shell command line with <c>bash -c</c> and collects what it wrote. For the opt-in passes
/// that change the environment under the suite: restarting a host (<see cref="E2EConfig.SocketRestartCommand"/>)
/// or taking a dependency down (<see cref="OutageControl"/>).
/// <para>
/// A command that leaves something running in the background must point that process's output
/// somewhere else (start-hosts.sh writes each host's to a log file): an inherited stdout keeps the
/// pipe read here open, and the read would wait for that process instead of the command.
/// </para>
/// </summary>
internal static class ShellCommand
{
    public sealed record Result(int ExitCode, string Output, string Error);

    /// <summary>
    /// Runs <paramref name="command"/> and returns its exit code and output. Throws a
    /// <see cref="TimeoutException"/> when it has not exited within <paramref name="timeout"/>.
    /// </summary>
    public static async Task<Result> RunAsync(string command, TimeSpan timeout)
    {
        using var process = Process.Start(new ProcessStartInfo("bash", ["-c", command])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(timeout);

        return new Result(process.ExitCode, await output, await error);
    }
}
