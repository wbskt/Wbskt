using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Webskt.Socket.Host.Infrastructure;
using Webskt.Socket.Host.Models;

namespace Webskt.Socket.Host.Services;

public interface ISocketHandler
{
    Task HandleAsync(HttpContext context);
}

public sealed class SocketHandler : ISocketHandler
{
    private readonly IConnectionManager _connectionManager;
    private readonly ILogger<SocketHandler> _logger;

    public SocketHandler(IConnectionManager connectionManager, ILogger<SocketHandler> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var clientRefIdString = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(clientRefIdString, out var clientRefId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        _connectionManager.AddConnection(clientRefId, webSocket);
        
        _logger.LogInformation("Client {ClientRefId} connected.", clientRefId);

        try
        {
            await ReceiveLoopAsync(clientRefId, webSocket);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in WebSocket loop for client {ClientRefId}.", clientRefId);
        }
        finally
        {
            await _connectionManager.RemoveConnectionAsync(clientRefId);
            _logger.LogInformation("Client {ClientRefId} disconnected.", clientRefId);
        }
    }

    private async Task ReceiveLoopAsync(Guid clientRefId, WebSocket webSocket)
    {
        var buffer = new byte[1024 * 4];

        while (webSocket.State == WebSocketState.Open)
        {
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Text)
            {
                var messageJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
                _logger.LogDebug("Received from {ClientRefId}: {Message}", clientRefId, messageJson);

                // Process message (In the future, publish to Event Bus)
                try
                {
                    var message = JsonSerializer.Deserialize<SocketMessage>(messageJson);
                    if (message != null)
                    {
                        // TODO: Dispatch to Workflow Engine
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Invalid JSON received from client {ClientRefId}.", clientRefId);
                }
            }
        }
    }
}
