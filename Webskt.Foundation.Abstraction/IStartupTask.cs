namespace Webskt.Foundation.Abstraction;

public interface IStartupTask
{
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}
