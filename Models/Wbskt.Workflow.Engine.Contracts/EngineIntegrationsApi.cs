using System.Text.Json;

namespace Wbskt.Workflow.Engine.Contracts;

/// <summary>
/// The engine's integration endpoints, which only the management host calls (with the same
/// <see cref="EngineInboundApi.ApiKeyHeader"/> key). The engine alone holds the master key, so
/// sealing a credential and trying one out both happen there; management stores and serves what
/// comes back and never sees a secret in the clear after the request that carried it.
/// Unlike <see cref="EngineInboundApi"/> these do not start runs, so any engine replica answers them.
/// </summary>
public static class EngineIntegrationsApi
{
    public const string SealRoute = "internal/integrations/seal";

    public const string TestRoute = "internal/integrations/test";
}

/// <summary>A secret as stored: the sealed bytes and the master key that sealed them.</summary>
public sealed record SealedSecret(byte[] Ciphertext, byte KeyId);

/// <summary>
/// Validates an integration's settings and seals its secret. <c>Secrets</c> is a patch over
/// <c>Existing</c> when that is given (a field left out keeps its value, an empty string clears an
/// optional one), and the whole secret otherwise. The type and preset are the API's names.
/// <c>ExistingConfig</c> is the settings <c>Existing</c> was saved with: when the new settings point
/// the credential somewhere else (another server, relay or origin), nothing saved is carried over and
/// the secret must be given again, so moving an integration cannot send its credential elsewhere.
/// </summary>
public sealed record IntegrationSealRequest(
    int WorkspaceId,
    Guid RefId,
    string Type,
    string? Preset,
    JsonElement? Config,
    JsonElement? Secrets,
    SealedSecret? Existing,
    JsonElement? ExistingConfig = null);

/// <summary>
/// The settings as they are to be stored (unknown fields dropped, defaults filled in), the secret
/// sealed afresh under the current master key, a hint that identifies it without revealing it, and
/// whether the secret itself changed (rather than only being resealed).
/// </summary>
public sealed record IntegrationSealResponse(
    JsonElement Config,
    SealedSecret Secret,
    string? SecretHint,
    bool SecretChanged);

/// <summary>What a test may do beyond checking the credential: send to a chat, or mail an address.</summary>
public sealed record IntegrationTestOptions(string? ChatId, string? SendTo);

/// <summary>
/// Tries an integration's settings and secret against the service, as <see cref="IntegrationSealRequest"/>
/// describes them, without storing anything.
/// </summary>
public sealed record IntegrationProbeRequest(
    int WorkspaceId,
    Guid RefId,
    string Type,
    string? Preset,
    JsonElement? Config,
    JsonElement? Secrets,
    SealedSecret? Existing,
    IntegrationTestOptions? Options,
    JsonElement? ExistingConfig = null);

/// <summary>
/// The test's outcome. A failed test is a successful answer: <c>Ok</c> false with a code and a
/// message safe to show (never the service's raw response, which can quote the credential).
/// <c>Metadata</c> is what the service told us about the account, such as a bot's username.
/// </summary>
public sealed record IntegrationProbeResponse(bool Ok, string? Code, string? Message, JsonElement? Metadata);
