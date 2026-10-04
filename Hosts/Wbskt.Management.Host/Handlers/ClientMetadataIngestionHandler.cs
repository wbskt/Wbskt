using System.Text.Json;
using MassTransit;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Management.Host.Models;
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

    // As the workflow engine judges a payload late: it arrived this long after the device sent it.
    internal static readonly TimeSpan LateThreshold = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions DeserializeOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IClientProvider _clientProvider;
    private readonly IClientReadingProvider _readingProvider;
    private readonly IEventBus _eventBus;
    private readonly ILogger<ClientMetadataIngestionHandler> _logger;

    public ClientMetadataIngestionHandler(
        IClientProvider clientProvider,
        IClientReadingProvider readingProvider,
        IEventBus eventBus,
        ILogger<ClientMetadataIngestionHandler> logger)
    {
        _clientProvider = clientProvider;
        _readingProvider = readingProvider;
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
        var readings = new List<ClientReadingValue>();
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

            var upsert = await _clientProvider.UpsertStateVariableAsync(message.ClientId, property.Name, dataType, valueJson, cancellationToken);
            if (!upsert.Stored)
            {
                _logger.LogWarning("Client {ClientRefId} is at its state variable limit; ignoring new variable '{Name}'.", message.ClientRefId, property.Name);
                continue;
            }

            // Numbers also go to the readings history, so they can be charted over time. Only for a
            // variable that was stored: the cap on variables also caps how many series a device makes.
            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out var number) && double.IsFinite(number))
            {
                readings.Add(new ClientReadingValue(property.Name, number));
            }

            if (upsert.OldValueJson != valueJson)
            {
                await _eventBus.PublishAsync(
                    new ClientPropertyUpdatedEvent(message.ClientRefId, message.ClientId, message.WorkspaceId, property.Name, upsert.OldValueJson, valueJson),
                    cancellationToken);
            }
        }

        if (readings.Count > 0)
        {
            // A buffered report counts at the time the device took it, not when it was finally sent.
            var receivedAt = message.CreatedAtUtc;
            var deviceTime = message.SentAtUtc ?? receivedAt;
            var isLate = receivedAt - deviceTime > LateThreshold;
            await _readingProvider.InsertAsync(message.ClientId, deviceTime, receivedAt, isLate, readings, cancellationToken);
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
