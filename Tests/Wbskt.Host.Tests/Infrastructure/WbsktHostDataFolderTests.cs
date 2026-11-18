using Wbskt.Infrastructure.Hosting;

namespace Wbskt.Host.Tests.Infrastructure;

public sealed class WbsktHostDataFolderTests
{
    [Fact]
    public void The_data_folder_is_a_full_path_whether_or_not_the_config_folder_existed()
    {
        // A relative "Wbskt" here meant the working directory, /app in the image, which the
        // unprivileged container user cannot write, and every host failed to start.
        Assert.True(Path.IsPathFullyQualified(WbsktHost.DataFolder), WbsktHost.DataFolder);
        Assert.Equal("Wbskt", Path.GetFileName(WbsktHost.DataFolder));
    }
}
