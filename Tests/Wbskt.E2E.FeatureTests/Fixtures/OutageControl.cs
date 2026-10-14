namespace Wbskt.E2E.FeatureTests.Fixtures;

/// <summary>
/// Takes a dependency of the hosts down and brings it back, through <see cref="E2EConfig.OutageCommand"/>
/// (in CI, <c>Tests/Wbskt.E2E.FeatureTests/outage.sh</c>). The scenarios under <c>Scenarios/Outage</c>
/// skip unless it is set, with <see cref="SkipReason"/>.
/// <para>
/// Every action waits until what it changed has settled (a container back up and healthy, an engine
/// answering, the original engine holding the lease again), so a scenario can carry on as soon as
/// one returns. Each is safe to repeat: bringing back something already up does nothing.
/// </para>
/// </summary>
internal static class OutageControl
{
    public const string SkipReason = "Stops the broker, Redis or an engine under the hosts. Set E2E_OUTAGE_COMMAND to run it.";

    public const string BrokerStop = "broker-stop";
    public const string BrokerStart = "broker-start";
    public const string RedisStop = "redis-stop";
    public const string RedisStart = "redis-start";

    /// <summary>Starts a second engine beside the first, which waits as a standby (<see cref="E2EConfig.StandbyEngineBaseUrl"/>).</summary>
    public const string SecondEngineStart = "engine2-start";

    /// <summary>Kills the engine that holds the lease, as a crash would: no shutdown, no lease released.</summary>
    public const string EngineKillLeader = "engine-kill-leader";

    /// <summary>Stops the second engine and returns once the original one is running and holds the lease again.</summary>
    public const string EngineRestore = "engine-restore";

    /// <summary>The broker, Redis and the original engine, all back as they were.</summary>
    public const string RestoreAll = "restore-all";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Runs one action and returns what it printed. Throws when it fails or does not finish in time.</summary>
    public static async Task<string> RunAsync(string action, TimeSpan? timeout = null)
    {
        var template = E2EConfig.OutageCommand
                       ?? throw new InvalidOperationException("E2E_OUTAGE_COMMAND is not set; the scenario should have skipped.");
        var command = template.Contains("{action}", StringComparison.Ordinal)
            ? template.Replace("{action}", action, StringComparison.Ordinal)
            : $"{template} {action}";

        var result = await ShellCommand.RunAsync(command, timeout ?? DefaultTimeout);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"'{command}' exited with {result.ExitCode}:\n{result.Output}\n{result.Error}");
        }

        return result.Output;
    }

    /// <summary>
    /// Runs <paramref name="scenario"/> and then <paramref name="restoreAction"/>, whether the scenario
    /// passed or not, so a failed scenario does not leave the next one without its broker, Redis or
    /// engine. A scenario that already restored what it took down only repeats a no-op here. When
    /// both fail, both failures are reported.
    /// </summary>
    public static async Task RunWithRestoreAsync(string restoreAction, Func<Task> scenario)
    {
        try
        {
            await scenario();
        }
        catch (Exception scenarioFailure)
        {
            try
            {
                await RunAsync(restoreAction);
            }
            catch (Exception restoreFailure)
            {
                throw new AggregateException("The scenario failed, and so did restoring what it took down.", scenarioFailure, restoreFailure);
            }

            throw;
        }

        await RunAsync(restoreAction);
    }
}
