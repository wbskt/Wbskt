using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Socket.Host.Infrastructure;
using Wbskt.Socket.Host.Models;

namespace Wbskt.Socket.Host.Services;

public interface ISocketHandler
{
    Task HandleAsync(HttpContext context);
}

internal sealed class SocketHandler : ISocketHandler
{
    private readonly IConnectionManager _connectionManager;
    private readonly ILogger<SocketHandler> _logger;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly IEventBus _eventBus;

    public SocketHandler(
        IConnectionManager connectionManager,
        ILogger<SocketHandler> logger,
        IHostApplicationLifetime appLifetime,
        IEventBus eventBus)
    {
        _connectionManager = connectionManager;
        _logger = logger;
        _appLifetime = appLifetime;
        _eventBus = eventBus;
    }

    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        // Create a linked token that triggers if the client leaves OR the server stops
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            context.RequestAborted, 
            _appLifetime.ApplicationStopping
        );

        var clientRefIdString = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var workspaceId = int.Parse(context.User.FindFirst("workspace_id")!.Value);
        if (!Guid.TryParse(clientRefIdString, out var clientRefId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync(); // we have multiple wbskt but not saving to connection manager
        _connectionManager.AddConnection(clientRefId, webSocket);
        
        _logger.LogInformation("Client {ClientRefId} connected.", clientRefId);
        await _eventBus.PublishAsync(new ClientConnectedEvent(clientRefId, workspaceId), cts.Token);

        try
        {
            // Use the HttpContext.RequestAborted token to detect when the underlying TCP connection is lost
            await ReceiveLoopAsync(clientRefId, webSocket, workspaceId, cts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Connection for client {ClientRefId} was cancelled.", clientRefId);
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning("WebSocket error for client {ClientRefId}: {Message}", clientRefId, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in WebSocket loop for client {ClientRefId}.", clientRefId);
        }
        finally
        {
            await _connectionManager.RemoveConnectionAsync(clientRefId, CancellationToken.None);
            _logger.LogInformation("Client {ClientRefId} disconnected and cleaned up.", clientRefId);
            await _eventBus.PublishAsync(new ClientDisconnectedEvent(clientRefId, workspaceId, "Socket closed"), CancellationToken.None);
        }
    }

    private async Task ReceiveLoopAsync(Guid clientRefId, WebSocket webSocket, int workspaceId,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 4];

        while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                _logger.LogInformation("Client {ClientRefId} initiated close.", clientRefId);
                await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Acknowledged", cancellationToken);
                break;
            }

            if (result.MessageType == WebSocketMessageType.Text)
            {
                var messageJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
                _logger.LogDebug("Received from {ClientRefId}: {Message}", clientRefId, messageJson);

                try
                {
                    var message = JsonSerializer.Deserialize<SocketMessage>(messageJson);
                    if (message != null)
                    {
                        await _eventBus.PublishAsync(new ClientPayloadReceivedEvent(clientRefId, workspaceId, message.Type, messageJson), cancellationToken);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning("Invalid JSON received from client {ClientRefId}: {Error}", clientRefId, ex.Message);
                }
            }
        }
    }
}
