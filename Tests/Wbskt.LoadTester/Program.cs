using System.Collections.Concurrent;
using System.Diagnostics;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.LoadTester;

Console.WriteLine("WBSKT Load Tester");
Console.WriteLine("=================");

if (args.Length < 2 || !int.TryParse(args[0], out var clientCount))
{
    Console.WriteLine("Usage: dotnet run -- <client_count> <policy_pin> [api_url] [socket_url]");
    return;
}

var policyPin = args[1];
var baseApiUrl = args.Length > 2 ? args[2] : "https://localhost:7010";
var baseSocketUrl = args.Length > 3 ? args[3] : "ws://localhost:5020";

var clients = new ConcurrentBag<IWbsktClient>();
var connectedCount = 0;
var stopwatch = Stopwatch.StartNew();

Console.WriteLine($"Attempting to spin up {clientCount} clients...");

var tasks = Enumerable.Range(0, clientCount).Select(i => Task.Run(async () =>
{
    var clientName = $"load-bot-{i:D4}";
    
    var config = new ClientConfig(
        BaseApiUrl: baseApiUrl,
        BaseSocketUrl: baseSocketUrl,
        DeviceName: clientName,
        PolicyPin: policyPin
    );

    // Using an in-memory storage to avoid file I/O bottlenecks
    var storage = new InMemoryClientStorage();
    var client = new WbsktClient(config, storage);

    client.OnConnected += () =>
    {
        Interlocked.Increment(ref connectedCount);
    };

    client.OnDisconnected += () =>
    {
        Interlocked.Decrement(ref connectedCount);
    };
    
    clients.Add(client);

    try
    {
        await client.StartAsync();

        // Start telemetry loop for this client
        _ = Task.Run(async () =>
        {
            var random = new Random();
            while (true)
            {
                await client.SendTelemetryAsync("performance", new { cpu = random.NextDouble() * 100 });
                await Task.Delay(TimeSpan.FromSeconds(5 + random.Next(10)));
            }
        });
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Failed to start client {clientName}: {ex.Message}");
        Console.ResetColor();
    }
}));

await Task.WhenAll(tasks);

stopwatch.Stop();
Console.WriteLine($"Client spin-up process finished in {stopwatch.Elapsed.TotalSeconds:F2} seconds.");

// Monitor loop
while (true)
{
    Console.SetCursorPosition(0, Console.CursorTop);
    Console.Write($"Connected clients: {connectedCount}/{clientCount}    (Press Ctrl+C to exit)");
    await Task.Delay(1000);
}


// A simple in-memory storage for the load test to avoid file contention
namespace Wbskt.LoadTester
{
    public class InMemoryClientStorage : IClientStorage
    {
        private Guid? _refId;
        private string? _secret;

        public Task SaveCredentialsAsync(Guid refId, string secret)
        {
            _refId = refId;
            _secret = secret;
            return Task.CompletedTask;
        }

        public Task<(Guid? RefId, string? Secret)> LoadCredentialsAsync()
        {
            return Task.FromResult((_refId, _secret));
        }

        public Task ClearCredentialsAsync()
        {
            _refId = null;
            _secret = null;
            return Task.CompletedTask;
        }
    }
}
