using Wbskt.Infrastructure;
using Wbskt.Workflow.Engine.Contracts;

namespace Wbskt.Management.Host.Services.Clients;

/// <summary>The engine's integration routes, called on the engine directly; see <see cref="EngineIntegrationClient"/>.</summary>
public interface IEngineIntegrationClient
{
    /// <summary>
    /// Has the engine validate an integration and seal its secret. The engine's refusal comes back as
    /// its error (a validation code); an engine that cannot be reached, or has no master key, as
    /// <see cref="ErrorType.Unavailable"/>.
    /// </summary>
    Task<Result<IntegrationSealResponse>> SealIntegrationAsync(IntegrationSealRequest request, CancellationToken ct);

    /// <summary>Has the engine try an integration against its service, with errors as <see cref="SealIntegrationAsync"/>.</summary>
    Task<Result<IntegrationProbeResponse>> ProbeIntegrationAsync(IntegrationProbeRequest request, CancellationToken ct);

    /// <summary>Has the engine ask a Telegram bot for its recent chats, with errors as <see cref="SealIntegrationAsync"/>.</summary>
    Task<Result<IntegrationChatsResponse>> GetTelegramChatsAsync(IntegrationChatsRequest request, CancellationToken ct);
}
