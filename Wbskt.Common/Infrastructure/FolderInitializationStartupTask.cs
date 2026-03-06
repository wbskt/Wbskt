using Wbskt.Foundation.Abstraction;
using Wbskt.Foundation.Abstraction.Constants;

namespace Wbskt.Common.Infrastructure;

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
