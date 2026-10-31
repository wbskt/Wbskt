using Wbskt.Client.Sdk;

namespace Wbskt.Host.Tests.ClientSdk;

public sealed class FileClientStorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"wbskt-storage-{Guid.NewGuid():N}");

    [Fact]
    public async Task Credentials_saved_are_loaded_back_and_cleared()
    {
        var storage = new FileClientStorage(Path.Combine(_dir, "nested", "device.json"));
        var refId = Guid.NewGuid();

        Assert.Equal((null, null), await storage.LoadCredentialsAsync());

        await storage.SaveCredentialsAsync(refId, "s3cret");
        Assert.Equal((refId, "s3cret"), await storage.LoadCredentialsAsync());

        await storage.ClearCredentialsAsync();
        Assert.Equal((null, null), await storage.LoadCredentialsAsync());
    }

    [Fact]
    public async Task A_corrupt_file_reads_as_no_credentials()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "device.json");
        await File.WriteAllTextAsync(path, "{ not json");

        Assert.Equal((null, null), await new FileClientStorage(path).LoadCredentialsAsync());
    }

    [Fact]
    public async Task Each_device_name_gets_its_own_file()
    {
        string a = $"sensor-{Guid.NewGuid():N}";
        string b = $"sensor/{Guid.NewGuid():N}";
        var first = FileClientStorage.ForDevice(a);
        var second = FileClientStorage.ForDevice(b);
        var refId = Guid.NewGuid();

        try
        {
            await first.SaveCredentialsAsync(refId, "one");
            Assert.Equal((null, null), await second.LoadCredentialsAsync());
        }
        finally
        {
            await first.ClearCredentialsAsync();
            await second.ClearCredentialsAsync();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
