namespace Wbskt.Host.Tests.Architecture;

/// <summary>Files of the repository this test run was built from, found by walking up from the test output.</summary>
internal static class RepoPaths
{
    public static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Wbskt.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Wbskt.slnx was not found above the test output.");
    }
}
