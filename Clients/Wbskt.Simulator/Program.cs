using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.Simulator;

Console.WriteLine("======================================");
Console.WriteLine("   WBSKT CLIENT SIMULATOR STARTING    ");
Console.WriteLine("======================================");

var config = new ClientConfig(
    BaseApiUrl: "https://localhost:7010", // Management API
    BaseSocketUrl: "wss://localhost:7020", // Socket Server
    DeviceName: $"Sim-Sensor-{Environment.TickCount % 10000}",
    PolicyPin: "123456"
);

var storage = new FileClientStorage();
await using var client = new WbsktClient(config, storage);

client.OnConnected += () => 
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("[CONNECTED] Handshake complete.");
    Console.ResetColor();
};

client.OnDisconnected += () => 
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("[DISCONNECTED] Connection lost.");
    Console.ResetColor();
};

client.OnMessageReceived += (action, payload, commandId) =>
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"[COMMAND] Action: {action}, Payload: {payload}, CommandId: {commandId ?? "-"}");
    Console.ResetColor();
};

try 
{
    Console.WriteLine("Initializing client lifecycle...");
    await client.StartAsync();

    Console.WriteLine("Client is active. Press Ctrl+C to exit.");

    var random = new Random();
    while (true)
    {
        var temp = 20 + random.NextDouble() * 10;
        var humidity = 40 + random.NextDouble() * 20;

        Console.WriteLine($"[TELEMETRY] Sending: {temp:F1}°C, {humidity:F1}% humidity");
        
        await client.SendAsync("telemetry", new { 
            temperature = temp, 
            humidity = humidity,
            timestamp = DateTime.UtcNow 
        });

        await Task.Delay(5000);
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[FATAL ERROR] {ex.Message}");
    if (ex.InnerException != null) 
    {
        Console.WriteLine($"Inner: {ex.InnerException.Message}");
    }
    Console.ResetColor();
}