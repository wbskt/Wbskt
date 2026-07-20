using System.Text.Json;
using MassTransit;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Management.Host.Providers;
using Wbskt.Models;

namespace Wbskt.Management.Host.Handlers;

// Destructures reserved client message types into durable per-client state. Ordinary payloads
// (telemetry etc.) stay in the workflow-trigger/event-log pipeline and are ignored here.
public sealed class ClientMetadataIngestionHandler : IConsumer<ClientMessageReceivedEvent>
{
    private const string CapabilitiesType = "capabilities";
    private const string StateReportType = "state.report";
    // Devices write straight into NVARCHAR(MAX); cap what a single report may carry.
    private const int MaxCapabilitiesPayloadChars = 32 * 1024;
    private const int MaxStatePayloadChars = 8 * 1024;
    private const int MaxStateVariablesPerReport = 50;
    private const int MaxStateVariableNameLength = 100;

    private static readonly JsonSerializerOptions DeserializeOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IClientProvider _clientProvider;
    private readonly IEventBus _eventBus;
    private readonly ILogger<ClientMetadataIngestionHandler> _logger;

    public ClientMetadataIngestionHandler(
        IClientProvider clientProvider,
        IEventBus eventBus,
        ILogger<ClientMetadataIngestionHandler> logger)
    {
        _clientProvider = clientProvider;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ClientMessageReceivedEvent> context)
    {
        var message = context.Message;

        switch (message.Type)
        {
            case CapabilitiesType:
                await IngestCapabilitiesAsync(message, context.CancellationToken);
                break;
            case StateReportType:
                await IngestStateReportAsync(message, context.CancellationToken);
                break;
        }
    }

    private async Task IngestStateReportAsync(ClientMessageReceivedEvent message, CancellationToken cancellationToken)
    {
        if (message.Payload.Length > MaxStatePayloadChars)
        {
            _logger.LogWarning("Discarding oversized state report ({Length} chars) from client {ClientRefId}.", message.Payload.Length, message.ClientRefId);
            return;
        }

        using var doc = ParseOrNull(message.Payload);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            _logger.LogWarning("Discarding state report from client {ClientRefId}: payload is not a JSON object.", message.ClientRefId);
            return;
        }

        var processed = 0;
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (++processed > MaxStateVariablesPerReport)
            {
                _logger.LogWarning("State report from client {ClientRefId} exceeds {Max} variables; ignoring the rest.", message.ClientRefId, MaxStateVariablesPerReport);
                break;
            }

            if (property.Name.Length is 0 or > MaxStateVariableNameLength)
            {
                _logger.LogWarning("Skipping state variable with invalid name length from client {ClientRefId}.", message.ClientRefId);
                continue;
            }

            var valueJson = property.Value.GetRawText();
            var dataType = GetDataType(property.Value.ValueKind);

            var oldValueJson = await _clientProvider.UpsertStateVariableAsync(message.ClientId, property.Name, dataType, valueJson, cancellationToken);
            if (oldValueJson != valueJson)
            {
                await _eventBus.PublishAsync(
                    new ClientPropertyUpdatedEvent(message.ClientRefId, message.ClientId, message.WorkspaceId, property.Name, oldValueJson, valueJson),
                    cancellationToken);
            }
        }
    }

    private static JsonDocument? ParseOrNull(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetDataType(JsonValueKind kind)
    {
        return kind switch
        {
            JsonValueKind.Number => "number",
            JsonValueKind.String => "string",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Array => "array",
            JsonValueKind.Object => "object",
            _ => "null"
        };
    }

    private async Task IngestCapabilitiesAsync(ClientMessageReceivedEvent message, CancellationToken cancellationToken)
    {
        if (message.Payload.Length > MaxCapabilitiesPayloadChars)
        {
            _logger.LogWarning("Discarding oversized capabilities payload ({Length} chars) from client {ClientRefId}.", message.Payload.Length, message.ClientRefId);
            return;
        }

        ClientCapabilities? capabilities;
        try
        {
            capabilities = JsonSerializer.Deserialize<ClientCapabilities>(message.Payload, DeserializeOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Discarding malformed capabilities payload from client {ClientRefId}: {Error}", message.ClientRefId, ex.Message);
            return;
        }

        if (capabilities is null || string.IsNullOrWhiteSpace(capabilities.Agent))
        {
            _logger.LogWarning("Discarding capabilities payload without an agent from client {ClientRefId}.", message.ClientRefId);
            return;
        }

        var agentName = Truncate(capabilities.Agent, 100);
        var agentVersion = Truncate(capabilities.Version ?? string.Empty, 50);
        var platform = Truncate(capabilities.OS ?? string.Empty, 100);
        var capabilitiesJson = JsonSerializer.Serialize(capabilities.Capabilities ?? []);

        await _clientProvider.UpsertCapabilitiesAsync(message.ClientId, agentName, agentVersion, platform, capabilitiesJson, cancellationToken);
        _logger.LogDebug("Stored capabilities for client {ClientRefId} ({Agent} {Version} on {Platform}).", message.ClientRefId, agentName, agentVersion, platform);

        await _eventBus.PublishAsync(
            new ClientCapabilitiesUpdatedEvent(message.ClientRefId, message.ClientId, message.WorkspaceId, agentName, agentVersion, platform),
            cancellationToken);
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
