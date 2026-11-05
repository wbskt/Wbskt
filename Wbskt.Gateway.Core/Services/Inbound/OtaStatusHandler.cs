using System.Text.Json;
using Wbskt.Protocol;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;

namespace Wbskt.Gateway.Core.Services.Inbound;

/// <summary>
/// <c>sys.ota.status</c>: how a firmware update is going, from accepted to installed, or why it was
/// rejected or failed. Published as <see cref="ClientOtaStatusEvent"/>, which reaches the live client
/// page and the client's activity.
/// </summary>
public sealed class OtaStatusHandler : IInboundMessageHandler
{
    internal const int MaxVersionLength = 50;
    internal const int MaxErrorLength = 500;

    private readonly IEventBus _eventBus;
    private readonly ILogger<OtaStatusHandler> _logger;

    public OtaStatusHandler(IEventBus eventBus, ILogger<OtaStatusHandler> logger)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    public string Type => MessageTypes.OtaStatus;

    public async Task HandleAsync(InboundContext context, ProtocolMessage message, CancellationToken cancellationToken)
    {
        if (message.Payload is not JsonElement { ValueKind: JsonValueKind.Object } payload
            || ReadString(payload, "state") is not { } state
            || !OtaStates.All.Contains(state))
        {
            _logger.LogDebug("Ignoring sys.ota.status from {ClientRefId} without a known state.", context.ClientRefId);
            return;
        }

        var connection = context.Connection;
        Guid? commandId = Guid.TryParse(ReadString(payload, "commandId"), out var id) ? id : null;
        int? progress = payload.TryGetProperty("progress", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var percent)
            ? Math.Clamp(percent, 0, 100)
            : null;

        await _eventBus.PublishAsync(
            new ClientOtaStatusEvent(
                context.ClientRefId, connection.ClientId, connection.WorkspaceId, state, commandId, progress,
                Truncate(ReadString(payload, "version"), MaxVersionLength),
                Truncate(ReadString(payload, "error"), MaxErrorLength)),
            cancellationToken);
    }

    private static string? ReadString(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= maxLength ? value : value[..maxLength];
}
