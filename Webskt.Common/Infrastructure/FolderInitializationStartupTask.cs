using Webskt.Foundation.Abstraction;
using Webskt.Foundation.Abstraction.Constants;

namespace Webskt.Common.Infrastructure;

public sealed class FolderInitializationStartupTask : IStartupTask
{
    private static readonly string ProgramDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Application.AppFolderName);

    public Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(ProgramDataPath))
        {
            Directory.CreateDirectory(ProgramDataPath);
        }

        return Task.CompletedTask;
    }
}
