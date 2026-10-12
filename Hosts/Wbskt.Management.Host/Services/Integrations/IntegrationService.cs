using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Integrations;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Models;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Engine.Contracts;

namespace Wbskt.Management.Host.Services.Integrations;

internal sealed class IntegrationService(
    IIntegrationProvider provider,
    IWorkflowEngineClient engine,
    IIdentityService identityService,
    IEventBus eventBus,
    IConfiguration configuration,
    ILogger<IntegrationService> logger) : IIntegrationService
{
    public const int DefaultMaxPerWorkspace = 50;
    public const int MaxNameLength = 100;

    private const int LimitReachedError = 50031; // THROW in dbo.Integration_Create
    private const int UniqueViolation = 2627;
    private const int UniqueIndexViolation = 2601;

    public static readonly Error NotFound = Error.NotFound("INTEGRATION_NOT_FOUND", "Integration not found.");

    private static readonly Error NameTaken = Error.Conflict("INTEGRATION_NAME_TAKEN", "Another integration in this workspace already has that name.");

    private static readonly Error Changed = Error.Conflict(
        "INTEGRATION_CHANGED",
        "This integration was changed by someone else while you were saving. Reload it and try again.");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private int MaxPerWorkspace => configuration.GetValue("Integrations:MaxPerWorkspace", DefaultMaxPerWorkspace);

    public async Task<Result<IReadOnlyList<IntegrationResponse>>> GetAllAsync(int workspaceId, string? type, CancellationToken ct)
    {
        IntegrationType? filter = null;
        if (!string.IsNullOrEmpty(type))
        {
            if (!IntegrationTypes.TryParse(type, out var parsed))
            {
                return Result<IReadOnlyList<IntegrationResponse>>.Failure(TypeInvalid);
            }

            filter = parsed;
        }

        var rows = await provider.GetAllAsync(workspaceId, filter, ct);
        return Result<IReadOnlyList<IntegrationResponse>>.Success(rows.Select(Map).ToList());
    }

    public async Task<Result<IntegrationResponse>> GetAsync(int workspaceId, Guid refId, CancellationToken ct)
    {
        var owned = await FindOwnedAsync(workspaceId, refId, ct);
        return owned.IsFailure
            ? Result<IntegrationResponse>.Failure(owned.Error)
            : Result<IntegrationResponse>.Success(Map(owned.Value));
    }

    public async Task<Result<IntegrationResponse>> CreateAsync(int workspaceId, IntegrationCreateRequest request, CancellationToken ct)
    {
        var name = ValidateName(request.Name);
        if (name.IsFailure)
        {
            return Result<IntegrationResponse>.Failure(name.Error);
        }

        if (!IntegrationTypes.TryParse(request.Type, out var type))
        {
            return Result<IntegrationResponse>.Failure(TypeInvalid);
        }

        IntegrationPreset? preset = null;
        if (request.Preset is not null)
        {
            if (!IntegrationTypes.TryParsePreset(request.Preset, out var parsedPreset) || IntegrationTypes.TypeOf(parsedPreset) != type)
            {
                return Result<IntegrationResponse>.Failure(Error.Validation("INTEGRATION_PRESET_INVALID", $"'{request.Preset}' is not a {request.Type} preset."));
            }

            preset = parsedPreset;
        }

        // The seal binds the secret to this RefId, so it is chosen here rather than by the database.
        Guid refId = Guid.NewGuid();
        var sealedResult = await engine.SealIntegrationAsync(
            new IntegrationSealRequest(workspaceId, refId, request.Type, request.Preset, request.Config, request.Secrets, null), ct);
        if (sealedResult.IsFailure)
        {
            return Result<IntegrationResponse>.Failure(sealedResult.Error);
        }

        var sealedIntegration = sealedResult.Value;
        var identity = identityService.GetUserIdentity();
        try
        {
            await provider.InsertAsync(new Integration
            {
                RefId = refId,
                WorkspaceId = workspaceId,
                Type = type,
                Preset = preset,
                Name = name.Value,
                ConfigJson = sealedIntegration.Config.GetRawText(),
                SecretCiphertext = sealedIntegration.Secret.Ciphertext,
                KeyId = sealedIntegration.Secret.KeyId,
                SecretHint = sealedIntegration.SecretHint,
                CreatedBy = identity.UserId,
                CreatedByRefId = identity.UserRefId
            }, MaxPerWorkspace, ct);
        }
        catch (SqlException ex) when (ex.Number == LimitReachedError)
        {
            return Result<IntegrationResponse>.Failure(Error.Conflict(
                "INTEGRATION_LIMIT_REACHED",
                $"A workspace can have at most {MaxPerWorkspace} integrations. Delete one you no longer use."));
        }
        catch (SqlException ex) when (ex.Number is UniqueViolation or UniqueIndexViolation)
        {
            return Result<IntegrationResponse>.Failure(NameTaken);
        }

        logger.LogInformation("Integration {RefId} ({Type}) added in WorkspaceId: {WorkspaceId}", refId, request.Type, workspaceId);
        await eventBus.PublishAsync(new IntegrationCreatedEvent(refId, workspaceId, name.Value, IntegrationTypes.NameOf(type)), ct);

        return await GetAsync(workspaceId, refId, ct);
    }

    public async Task<Result<IntegrationResponse>> UpdateAsync(int workspaceId, Guid refId, IntegrationUpdateRequest request, CancellationToken ct)
    {
        var owned = await FindOwnedAsync(workspaceId, refId, ct);
        if (owned.IsFailure)
        {
            return Result<IntegrationResponse>.Failure(owned.Error);
        }

        var existing = owned.Value;
        string typeName = IntegrationTypes.NameOf(existing.Type);
        if (request.Type is not null && request.Type != typeName)
        {
            return Result<IntegrationResponse>.Failure(TypeImmutable(typeName));
        }

        string newName = existing.Name;
        if (request.Name is not null)
        {
            var name = ValidateName(request.Name);
            if (name.IsFailure)
            {
                return Result<IntegrationResponse>.Failure(name.Error);
            }

            newName = name.Value;
        }

        string configJson = existing.ConfigJson;
        IntegrationSealResponse? resealed = null;
        if (request.Config is not null || request.Secrets is not null)
        {
            // A secrets-only change keeps the saved settings; the engine merges the patch into the sealed secret.
            JsonElement config = request.Config ?? JsonSerializer.Deserialize<JsonElement>(existing.ConfigJson);
            var sealedResult = await engine.SealIntegrationAsync(new IntegrationSealRequest(
                workspaceId,
                refId,
                typeName,
                existing.Preset is { } preset ? IntegrationTypes.NameOf(preset) : null,
                config,
                request.Secrets,
                new SealedSecret(existing.SecretCiphertext, existing.KeyId),
                JsonSerializer.Deserialize<JsonElement>(existing.ConfigJson)), ct);
            if (sealedResult.IsFailure)
            {
                return Result<IntegrationResponse>.Failure(sealedResult.Error);
            }

            resealed = sealedResult.Value;
            configJson = resealed.Config.GetRawText();
        }

        // Nothing to save: writing anyway would move UpdatedAt and drop a test result still in flight.
        if (newName == existing.Name && resealed is null)
        {
            return Result<IntegrationResponse>.Success(Map(existing));
        }

        bool configChanged = configJson != existing.ConfigJson;
        bool secretRotated = resealed?.SecretChanged == true;
        try
        {
            bool updated = await provider.UpdateAsync(
                workspaceId,
                existing.Id,
                newName,
                configJson,
                configChanged,
                resealed?.Secret.Ciphertext,
                resealed?.Secret.KeyId,
                resealed?.SecretHint,
                secretRotated,
                existing.UpdatedAt,
                ct);
            if (!updated)
            {
                return Result<IntegrationResponse>.Failure(Changed);
            }
        }
        catch (SqlException ex) when (ex.Number is UniqueViolation or UniqueIndexViolation)
        {
            return Result<IntegrationResponse>.Failure(NameTaken);
        }

        var changes = new List<FieldChange>();
        if (newName != existing.Name)
        {
            changes.Add(new FieldChange("name", existing.Name, newName));
        }

        if (configChanged)
        {
            changes.AddRange(ConfigChanges(existing.ConfigJson, configJson));
        }

        if (changes.Count > 0)
        {
            await eventBus.PublishAsync(new IntegrationUpdatedEvent(refId, workspaceId, newName, typeName) { Changes = changes }, ct);
        }

        if (secretRotated)
        {
            // Which secret, never its value.
            await eventBus.PublishAsync(new IntegrationSecretRotatedEvent(refId, workspaceId, newName, typeName)
            {
                Changes = [new FieldChange("secretHint", existing.SecretHint, resealed!.SecretHint)]
            }, ct);
        }

        logger.LogInformation("Integration {RefId} updated in WorkspaceId: {WorkspaceId} (settings changed: {ConfigChanged}, secret replaced: {SecretRotated})", refId, workspaceId, configChanged, secretRotated);
        return await GetAsync(workspaceId, refId, ct);
    }

    public async Task<IntegrationDeleteResult> DeleteAsync(int workspaceId, Guid refId, CancellationToken ct)
    {
        var owned = await FindOwnedAsync(workspaceId, refId, ct);
        if (owned.IsFailure)
        {
            return new IntegrationDeleteResult(Result.Failure(owned.Error), []);
        }

        var integration = owned.Value;
        var blocking = await provider.DeleteAsync(workspaceId, integration.Id, ct);
        if (blocking.Count > 0)
        {
            int workflows = blocking.Select(u => u.WorkflowRefId).Distinct().Count();
            return new IntegrationDeleteResult(
                Result.Failure(Error.Conflict(
                    "INTEGRATION_IN_USE",
                    $"'{integration.Name}' is used by {workflows} published workflow{(workflows == 1 ? string.Empty : "s")}. Remove it from their steps, or deprecate them, first.")),
                blocking.Select(MapUse).ToList());
        }

        logger.LogInformation("Integration {RefId} deleted in WorkspaceId: {WorkspaceId}", refId, workspaceId);
        await eventBus.PublishAsync(new IntegrationDeletedEvent(refId, workspaceId, integration.Name, IntegrationTypes.NameOf(integration.Type)), ct);
        return new IntegrationDeleteResult(Result.Success(), []);
    }

    public async Task<Result<IntegrationUsageResponse>> GetUsageAsync(int workspaceId, Guid refId, CancellationToken ct)
    {
        var owned = await FindOwnedAsync(workspaceId, refId, ct);
        if (owned.IsFailure)
        {
            return Result<IntegrationUsageResponse>.Failure(owned.Error);
        }

        var uses = await provider.GetUsageAsync(owned.Value.Id, ct);
        return Result<IntegrationUsageResponse>.Success(new IntegrationUsageResponse(uses.Select(MapUse).ToList()));
    }

    public async Task<Result<IntegrationTestResponse>> TestAsync(int workspaceId, IntegrationTestRequest request, CancellationToken ct)
    {
        Integration? saved = null;
        string? typeName = request.Type;
        string? presetName = request.Preset;
        JsonElement? config = request.Config;

        if (request.RefId is { } refId)
        {
            var owned = await FindOwnedAsync(workspaceId, refId, ct);
            if (owned.IsFailure)
            {
                return Result<IntegrationTestResponse>.Failure(owned.Error);
            }

            saved = owned.Value;
            string savedType = IntegrationTypes.NameOf(saved.Type);
            if (typeName is not null && typeName != savedType)
            {
                return Result<IntegrationTestResponse>.Failure(TypeImmutable(savedType));
            }

            typeName = savedType;
            presetName = saved.Preset is { } preset ? IntegrationTypes.NameOf(preset) : null;
            config ??= JsonSerializer.Deserialize<JsonElement>(saved.ConfigJson);
        }
        else if (typeName is null || !IntegrationTypes.TryParse(typeName, out _))
        {
            return Result<IntegrationTestResponse>.Failure(TypeInvalid);
        }

        var probe = await engine.ProbeIntegrationAsync(new IntegrationProbeRequest(
            workspaceId,
            saved?.RefId ?? Guid.NewGuid(),
            typeName,
            presetName,
            config,
            request.Secrets,
            saved is null ? null : new SealedSecret(saved.SecretCiphertext, saved.KeyId),
            request.Options is null ? null : new IntegrationTestOptions(request.Options.ChatId, request.Options.SendTo),
            saved is null ? null : JsonSerializer.Deserialize<JsonElement>(saved.ConfigJson)), ct);
        if (probe.IsFailure)
        {
            return Result<IntegrationTestResponse>.Failure(probe.Error);
        }

        var outcome = probe.Value;

        // Only a test of what is saved describes the integration. A test of unsaved edits says nothing
        // about it yet, and one that raced an edit is dropped by the procedure.
        if (saved is not null && request.Config is null && request.Secrets is null)
        {
            bool recorded = await provider.RecordTestAsync(
                saved.Id,
                saved.UpdatedAt,
                outcome.Ok,
                outcome.Code,
                outcome.Message,
                outcome.Ok && outcome.Metadata is { } metadata ? metadata.GetRawText() : null,
                ct);
            if (recorded)
            {
                await eventBus.PublishAsync(new IntegrationTestedEvent(saved.RefId, workspaceId, saved.Name, typeName, outcome.Ok, outcome.Code), ct);
            }
        }

        return Result<IntegrationTestResponse>.Success(new IntegrationTestResponse(outcome.Ok, outcome.Code, outcome.Message, outcome.Metadata));
    }

    private async Task<Result<Integration>> FindOwnedAsync(int workspaceId, Guid refId, CancellationToken ct) =>
        await WorkspaceOwnership.LoadAsync(workspaceId, () => provider.FindByRefIdAsync(refId, ct), NotFound);

    private static Result<string> ValidateName(string? name)
    {
        string? trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxNameLength || trimmed.Any(char.IsControl)
            ? Result<string>.Failure(Error.Validation("INTEGRATION_NAME_INVALID", $"Name must be 1-{MaxNameLength} characters on one line."))
            : Result<string>.Success(trimmed);
    }

    private static Error TypeInvalid => Error.Validation(
        "INTEGRATION_TYPE_INVALID",
        $"type must be one of {string.Join(", ", IntegrationTypes.AllNames)}.");

    private static Error TypeImmutable(string current) => Error.Validation(
        "INTEGRATION_TYPE_IMMUTABLE",
        $"This is a {current} integration, and a type cannot change. Add a new integration instead.");

    /// <summary>The settings fields that differ, with their values: settings are never secret.</summary>
    private static IEnumerable<FieldChange> ConfigChanges(string beforeJson, string afterJson)
    {
        using var before = JsonDocument.Parse(beforeJson);
        using var after = JsonDocument.Parse(afterJson);
        var fields = before.RootElement.EnumerateObject().Select(p => p.Name)
            .Union(after.RootElement.EnumerateObject().Select(p => p.Name))
            .OrderBy(n => n, StringComparer.Ordinal);

        foreach (var field in fields)
        {
            string? was = Value(before.RootElement, field);
            string? now = Value(after.RootElement, field);
            if (was != now)
            {
                yield return new FieldChange($"config.{field}", was, now);
            }
        }

        static string? Value(JsonElement element, string field) =>
            element.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null
                ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText()
                : null;
    }

    private static IntegrationUsedBy MapUse(IntegrationUse use) =>
        new(use.WorkflowRefId, use.WorkflowName, use.NodeId, use.NodeName, use.IsEnabled ? "published" : "deprecated");

    private static IntegrationResponse Map(Integration i) => new(
        i.RefId,
        IntegrationTypes.NameOf(i.Type),
        IntegrationTypes.CategoryName(i.Type),
        i.Preset is { } preset ? IntegrationTypes.NameOf(preset) : null,
        i.Name,
        JsonSerializer.Deserialize<JsonElement>(i.ConfigJson),
        i.SecretHint,
        i.Metadata is null ? null : JsonSerializer.Deserialize<JsonElement>(i.Metadata),
        i.LastTestAt is { } at ? new IntegrationLastTest(at, i.LastTestOk == true, i.LastTestCode, i.LastTestError) : null,
        i.LastUsedAt,
        new IntegrationUsageSummary(i.UsageWorkflows, i.UsageSteps),
        new IntegrationCreator(i.CreatedByRefId),
        i.CreatedAt,
        i.UpdatedAt,
        i.SecretRotatedAt);
}
