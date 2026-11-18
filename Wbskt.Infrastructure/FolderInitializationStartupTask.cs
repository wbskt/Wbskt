using Wbskt.Primitives;
using Wbskt.Infrastructure.Hosting;

namespace Wbskt.Infrastructure;

public sealed class FolderInitializationStartupTask : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(WbsktHost.DataFolder);
        return Task.CompletedTask;
    }
}
