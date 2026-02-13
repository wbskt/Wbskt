namespace Webskt.Common.Abstraction.Interfaces;

public interface IStartupTask
{
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}
