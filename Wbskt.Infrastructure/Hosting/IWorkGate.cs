namespace Wbskt.Infrastructure.Hosting;

/// <summary>
/// Whether a <see cref="PeriodicWorker"/> may work on this tick. The engine's gate is open only on the
/// active instance; a host that runs on every instance uses <see cref="AlwaysOpenWorkGate"/>.
/// </summary>
public interface IWorkGate
{
    bool IsOpen { get; }
}

/// <summary>A gate for workers that run on every instance of their host.</summary>
public sealed class AlwaysOpenWorkGate : IWorkGate
{
    public static readonly AlwaysOpenWorkGate Instance = new();

    private AlwaysOpenWorkGate()
    {
    }

    public bool IsOpen => true;
}
