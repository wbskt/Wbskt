using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Integrations;

/// <summary>
/// Gives a step the integration it names, ready to send through, or the reason it cannot have it.
/// Read fresh on every send (no cache): a rotated secret or a deleted integration takes effect on
/// the next step, and nothing holds an opened secret longer than a send.
/// </summary>
internal interface IIntegrationResolver
{
    Task<IntegrationResolution> ResolveAsync(BranchContext branch, Guid? refId, IntegrationType expected, CancellationToken ct);

    /// <summary>Records that a run just sent through it. Best effort: a failure here never fails the step.</summary>
    Task MarkUsedAsync(ResolvedIntegration integration, CancellationToken ct);
}

internal sealed record IntegrationResolution(ResolvedIntegration? Integration, NodeExecutionResult.Fail? Failure);

internal sealed class IntegrationResolver(
    IIntegrationStore store,
    IntegrationVault vault,
    IOptions<IntegrationOptions> options,
    ILogger<IntegrationResolver> logger) : IIntegrationResolver
{
    public async Task<IntegrationResolution> ResolveAsync(BranchContext branch, Guid? refId, IntegrationType expected, CancellationToken ct)
    {
        if (refId is not { } reference || reference == Guid.Empty)
        {
            return Failed("INTEGRATION_NOT_FOUND", $"The step names no {IntegrationTypes.NameOf(expected)} integration to send through.");
        }

        var row = await store.FindSealedAsync(reference, ct);

        // Another workspace's integration is as absent as one that does not exist.
        if (row is null || row.WorkspaceId != branch.WorkspaceId)
        {
            return Failed("INTEGRATION_NOT_FOUND", $"Integration {reference} does not exist in this workspace; it may have been deleted.");
        }

        if (row.Type != expected)
        {
            return Failed("INTEGRATION_WRONG_TYPE", $"Integration {reference} is a {IntegrationTypes.NameOf(row.Type)} integration; this step needs {IntegrationTypes.NameOf(expected)}.");
        }

        IntegrationSettings settings;
        using (var config = JsonDocument.Parse(row.ConfigJson))
        {
            var parsed = IntegrationShape.ParseSettings(row.Type, row.Preset, config.RootElement.Clone(), options.Value);
            if (parsed.IsFailure)
            {
                return Failed("INTEGRATION_INVALID", $"Integration {reference} is no longer valid: {parsed.Error.Message}");
            }

            settings = parsed.Value;
        }

        var secrets = vault.Open(row.WorkspaceId, row.RefId, row.Type, row.Secret);
        if (secrets.IsFailure)
        {
            logger.LogError("Integration {RefId} could not be opened: {Code}", row.RefId, secrets.Error.Code);
            return Failed("INTEGRATION_UNREADABLE", $"Integration {reference}'s secret could not be opened on this engine.");
        }

        return new IntegrationResolution(new ResolvedIntegration(row.Id, row.RefId, row.WorkspaceId, row.Type, row.Preset, settings, secrets.Value), null);
    }

    public async Task MarkUsedAsync(ResolvedIntegration integration, CancellationToken ct)
    {
        if (integration.Id is not { } id)
        {
            return;
        }

        try
        {
            await store.TouchLastUsedAsync(id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not record that integration {RefId} was used.", integration.RefId);
        }
    }

    // Never worth retrying: no number of attempts brings back a deleted integration or changes its type.
    private static IntegrationResolution Failed(string code, string message) =>
        new(null, new NodeExecutionResult.Fail(code, message, false, null));
}
