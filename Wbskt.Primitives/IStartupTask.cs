namespace Wbskt.Primitives;

public interface IStartupTask
{
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}
