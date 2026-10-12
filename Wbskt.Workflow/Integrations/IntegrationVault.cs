using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Engine.Contracts;

namespace Wbskt.Workflow.Integrations;

/// <summary>
/// What the management host asks of the engine for an integration: validate and seal it, or try it.
/// Both take the settings and a secret patch over what is already sealed, so the management host
/// never needs a secret in the clear to change one field of it.
/// </summary>
public interface IIntegrationVault
{
    Result<IntegrationSealResponse> Seal(IntegrationSealRequest request);

    Task<Result<IntegrationProbeResponse>> TestAsync(IntegrationProbeRequest request, CancellationToken ct);
}

internal sealed class IntegrationVault(
    IntegrationCipher cipher,
    IOptions<IntegrationOptions> options,
    IEnumerable<IIntegrationChannel> channels) : IIntegrationVault
{
    public static readonly Error NotConfigured = Error.Unavailable(
        "INTEGRATIONS_NOT_CONFIGURED",
        "Integrations are not set up on this server: the engine has no master key.");

    private static readonly Error Unreadable = Error.Validation(
        "INTEGRATION_SECRET_UNREADABLE",
        "The saved secret could not be opened. Enter it again.");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IReadOnlyDictionary<IntegrationType, IIntegrationChannel> _channels = channels.ToDictionary(c => c.Type);

    public Result<IntegrationSealResponse> Seal(IntegrationSealRequest request)
    {
        if (!cipher.IsConfigured)
        {
            return Result<IntegrationSealResponse>.Failure(NotConfigured);
        }

        var prepared = Prepare(request.WorkspaceId, request.RefId, request.Type, request.Preset, request.Config, request.Secrets, request.Existing);
        if (prepared.IsFailure)
        {
            return Result<IntegrationSealResponse>.Failure(prepared.Error);
        }

        var (integration, secretChanged) = prepared.Value;
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(integration.Secrets, Json);
        try
        {
            var sealedSecret = cipher.Seal(request.WorkspaceId, request.RefId, integration.Type, plaintext);
            return Result<IntegrationSealResponse>.Success(new IntegrationSealResponse(
                JsonSerializer.SerializeToElement(integration.Settings, integration.Settings.GetType(), Json),
                sealedSecret,
                IntegrationShape.Hint(integration.Type, integration.Secrets),
                secretChanged));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task<Result<IntegrationProbeResponse>> TestAsync(IntegrationProbeRequest request, CancellationToken ct)
    {
        if (!cipher.IsConfigured && request.Existing is not null)
        {
            return Result<IntegrationProbeResponse>.Failure(NotConfigured);
        }

        var prepared = Prepare(request.WorkspaceId, request.RefId, request.Type, request.Preset, request.Config, request.Secrets, request.Existing);
        if (prepared.IsFailure)
        {
            return Result<IntegrationProbeResponse>.Failure(prepared.Error);
        }

        var integration = prepared.Value.Integration;
        var result = await _channels[integration.Type].TestAsync(integration, request.Options, ct);
        return Result<IntegrationProbeResponse>.Success(new IntegrationProbeResponse(
            result.Ok,
            result.Code,
            result.Message is null ? null : IntegrationErrors.Scrub(result.Message, integration.Secrets),
            result.Metadata));
    }

    /// <summary>
    /// Parses and checks the settings, opens what is already sealed (when anything is), applies the
    /// patch, and checks the whole secret against the settings.
    /// </summary>
    private Result<(ResolvedIntegration Integration, bool SecretChanged)> Prepare(
        int workspaceId, Guid refId, string typeName, string? presetName, JsonElement? config, JsonElement? secrets, SealedSecret? existing)
    {
        if (!IntegrationTypes.TryParse(typeName, out var type))
        {
            return Fail(Error.Validation("INTEGRATION_TYPE_INVALID", $"type must be one of {string.Join(", ", IntegrationTypes.AllNames)}."));
        }

        IntegrationPreset? preset = null;
        if (presetName is not null)
        {
            if (!IntegrationTypes.TryParsePreset(presetName, out var parsedPreset))
            {
                return Fail(Error.Validation(IntegrationShape.PresetInvalid, $"'{presetName}' is not a preset."));
            }

            preset = parsedPreset;
        }

        var settings = IntegrationShape.ParseSettings(type, preset, config, options.Value);
        if (settings.IsFailure)
        {
            return Fail(settings.Error);
        }

        var patch = IntegrationShape.ParseSecrets(secrets);
        if (patch.IsFailure)
        {
            return Fail(patch.Error);
        }

        var fields = IntegrationShape.CheckFields(type, patch.Value);
        if (fields.IsFailure)
        {
            return Fail(fields.Error);
        }

        var stored = IntegrationSecrets.Empty;
        if (existing is not null)
        {
            var opened = Open(workspaceId, refId, type, existing);
            if (opened.IsFailure)
            {
                return Fail(opened.Error);
            }

            stored = opened.Value;
        }

        var merged = IntegrationShape.ValidateSecrets(type, settings.Value, IntegrationShape.Merge(stored, patch.Value), options.Value);
        if (merged.IsFailure)
        {
            return Fail(merged.Error);
        }

        bool changed = existing is null || merged.Value != stored;
        return Result<(ResolvedIntegration, bool)>.Success((
            new ResolvedIntegration(null, refId, workspaceId, type, preset, settings.Value, merged.Value),
            changed));
    }

    /// <summary>Opens a sealed secret into its fields.</summary>
    public Result<IntegrationSecrets> Open(int workspaceId, Guid refId, IntegrationType type, SealedSecret secret)
    {
        byte[] plaintext;
        try
        {
            plaintext = cipher.Open(workspaceId, refId, type, secret);
        }
        catch (IntegrationKeyUnavailableException)
        {
            return Result<IntegrationSecrets>.Failure(NotConfigured);
        }
        catch (CryptographicException)
        {
            return Result<IntegrationSecrets>.Failure(Unreadable);
        }

        try
        {
            return Result<IntegrationSecrets>.Success(JsonSerializer.Deserialize<IntegrationSecrets>(plaintext, Json) ?? IntegrationSecrets.Empty);
        }
        catch (JsonException)
        {
            return Result<IntegrationSecrets>.Failure(Unreadable);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static Result<(ResolvedIntegration, bool)> Fail(Error error) => Result<(ResolvedIntegration, bool)>.Failure(error);
}
