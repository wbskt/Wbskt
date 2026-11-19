using Wbskt.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Protocol;

namespace Wbskt.Infrastructure.Clients;

/// <summary>What became of a command handed to <see cref="IClientCommandSender"/>.</summary>
public enum ClientCommandOutcome
{
    /// <summary>On the bus, addressed to the socket host holding the client.</summary>
    Sent,

    /// <summary>
    /// The client is offline, so the command waits in its queue until it connects or the command
    /// expires (<see cref="ClientCommandResult.ExpiresAtUtc"/>).
    /// </summary>
    Queued,

    /// <summary>The client is offline and already has as many commands waiting as it may.</summary>
    QueueFull,

    /// <summary>The client is offline and not registered (pending, revoked or rejected), so nothing can wait for it.</summary>
    Inactive,

    /// <summary>No such client in the sender's workspace. A client of another workspace reads the same.</summary>
    UnknownClient,

    /// <summary>The type or payload breaks <see cref="ClientMessageRules"/>; see <see cref="ClientCommandResult.Violation"/>.</summary>
    Invalid,

    /// <summary>The broker failed or did not take the command in time; nothing was sent.</summary>
    BrokerUnavailable,

    /// <summary>The expiry has passed, or is further ahead than <see cref="CommandLifetime.Max"/>.</summary>
    ExpiryInvalid
}

/// <param name="ClientId">The client's internal id; 0 when <see cref="ClientCommandOutcome.UnknownClient"/>.</param>
/// <param name="ExpiresAtUtc">When a <see cref="ClientCommandOutcome.Queued"/> command stops waiting.</param>
public sealed record ClientCommandResult(ClientCommandOutcome Outcome, int ClientId = 0, ClientMessageViolation? Violation = null, DateTime? ExpiresAtUtc = null);

/// <param name="ExpiresAtUtc">Past this the socket host and the SDK refuse the command instead of acting on it late.</param>
/// <param name="Platform">A platform command such as <c>sys.ota.update</c>, which the protocol rules otherwise refuse as reserved.</param>
public sealed record ClientCommand(Guid CommandId, Guid ClientRefId, int WorkspaceId, string Type, string Payload, DateTime? ExpiresAtUtc = null, bool Platform = false);

/// <summary>
/// Sends a command to a client, the same way for the console and for a workflow step: resolve the
/// client in the sender's workspace, apply the protocol rules, and publish addressed to the socket
/// host that holds it, or queue it for an offline client to take when it connects. The caller maps
/// the outcome: management to HTTP, the engine to the step's port.
/// </summary>
public interface IClientCommandSender
{
    /// <param name="attribute">Sets who sent the command on its events, when the bus would not know.</param>
    Task<ClientCommandResult> SendAsync(ClientCommand command, Action<ActorEvent>? attribute, CancellationToken cancellationToken);

    /// <summary>
    /// The client as a command sees it, or null when the workspace has no such client (another
    /// workspace's reads the same). For a caller that must look at the client before it sends, such as
    /// a firmware update that needs the client to take updates; it passes the target on to
    /// <see cref="SendAsync(ClientCommand, ClientCommandTarget, Action{ActorEvent}?, CancellationToken)"/>.
    /// </summary>
    Task<ClientCommandTarget?> FindTargetAsync(Guid clientRefId, int workspaceId, CancellationToken cancellationToken);

    /// <summary><see cref="SendAsync(ClientCommand, Action{ActorEvent}?, CancellationToken)"/> to a target already found in the command's workspace.</summary>
    Task<ClientCommandResult> SendAsync(ClientCommand command, ClientCommandTarget target, Action<ActorEvent>? attribute, CancellationToken cancellationToken);
}

public sealed class ClientCommandSender(
    IClientCommandTargetProvider targets,
    ClientCommandQueue queue,
    IEventBus eventBus,
    TimeProvider clock,
    ILogger<ClientCommandSender> logger) : IClientCommandSender
{
    /// <summary>How long a command waits for the broker before the sender is told it is unavailable.</summary>
    internal static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(5);

    public async Task<ClientCommandResult> SendAsync(ClientCommand command, Action<ActorEvent>? attribute, CancellationToken cancellationToken)
    {
        var target = await FindTargetAsync(command.ClientRefId, command.WorkspaceId, cancellationToken);
        if (target is null)
        {
            logger.LogInformation("No client '{ClientRefId}' in workspace {WorkspaceId}; command '{Type}' not sent.", command.ClientRefId, command.WorkspaceId, command.Type);
            return new ClientCommandResult(ClientCommandOutcome.UnknownClient);
        }

        return await SendAsync(command, target, attribute, cancellationToken);
    }

    // Ownership has to be settled here: the command goes out over the bus and the socket host routes it
    // by ClientRefId alone, so nothing downstream would notice a client from another workspace.
    public async Task<ClientCommandTarget?> FindTargetAsync(Guid clientRefId, int workspaceId, CancellationToken cancellationToken)
    {
        var target = await targets.FindAsync(clientRefId, cancellationToken);
        return target is not null && target.WorkspaceId == workspaceId ? target : null;
    }

    public async Task<ClientCommandResult> SendAsync(ClientCommand command, ClientCommandTarget target, Action<ActorEvent>? attribute, CancellationToken cancellationToken)
    {
        if (target.WorkspaceId != command.WorkspaceId)
        {
            throw new ArgumentException("The target is not in the command's workspace.", nameof(target));
        }

        var violation = command.Platform
            ? ClientMessageRules.CheckPlatformCommand(command.Type, command.Payload)
            : ClientMessageRules.Check(command.Type, command.Payload);
        if (violation is not null)
        {
            logger.LogInformation("Command '{Type}' to client '{ClientRefId}' refused: {Reason}", command.Type, command.ClientRefId, violation.Message);
            return new ClientCommandResult(ClientCommandOutcome.Invalid, target.ClientId, violation);
        }

        // The lifetime is promised to every sender, so it is held here rather than by each caller.
        if (command.ExpiresAtUtc is { } expiresAt && !CommandLifetime.IsValidExpiry(new DateTimeOffset(DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc)), clock.GetUtcNow()))
        {
            logger.LogInformation("Command '{Type}' to client '{ClientRefId}' refused: it expires at {ExpiresAt:O}.", command.Type, command.ClientRefId, expiresAt);
            return new ClientCommandResult(ClientCommandOutcome.ExpiryInvalid, target.ClientId);
        }

        if (!target.IsConnected || string.IsNullOrEmpty(target.HostId))
        {
            return await QueueAsync(command, target, attribute, cancellationToken);
        }

        // Until commands go to the owning host's own queue (#278), a command for a host that crashed is
        // skipped by every live host and lost. A host that has gone quiet is treated as gone: the client
        // takes the command when it reconnects.
        if (queue.HostIsSilent(target))
        {
            logger.LogInformation("Client '{ClientRefId}' is held by socket host '{HostId}', silent for {Silent} s; queueing command '{CommandId}'.", command.ClientRefId, target.HostId, target.HostSilentSeconds?.ToString() ?? "unknown", command.CommandId);
            return await QueueAsync(command, target, attribute, cancellationToken);
        }

        var @event = new ClientCommandEvent(command.ClientRefId, target.ClientId, command.WorkspaceId, command.Type, command.Payload, command.CommandId,
            TargetHostId: target.HostId, ExpiresAtUtc: command.ExpiresAtUtc);
        attribute?.Invoke(@event);

        // The publish is the action: a command reported as sent must have reached the broker, so a
        // broker that fails or does not answer in time is reported, rather than a hung caller.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PublishTimeout);
        try
        {
            await eventBus.PublishAsync(@event, timeout.Token);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Publishing command '{CommandId}' failed; the event bus is unavailable. {Message}", command.CommandId, ex.Message);
            return new ClientCommandResult(ClientCommandOutcome.BrokerUnavailable, target.ClientId);
        }

        return new ClientCommandResult(ClientCommandOutcome.Sent, target.ClientId);
    }

    private async Task<ClientCommandResult> QueueAsync(ClientCommand command, ClientCommandTarget target, Action<ActorEvent>? attribute, CancellationToken cancellationToken)
    {
        // A client that is not registered will not sign in to take it.
        if (target.Status != ClientStatus.Registered)
        {
            logger.LogInformation("Client '{ClientRefId}' is offline and {Status}; command '{Type}' not queued.", command.ClientRefId, target.Status, command.Type);
            return new ClientCommandResult(ClientCommandOutcome.Inactive, target.ClientId);
        }

        var expiresAt = queue.ExpiryFor(command.ExpiresAtUtc);
        var announcement = new ClientCommandQueuedEvent(command.ClientRefId, target.ClientId, command.WorkspaceId, command.Type, command.Payload, command.CommandId, expiresAt);
        attribute?.Invoke(announcement);

        if (!await queue.QueueAsync(QueuedCommand.From(command, target.ClientId, expiresAt, announcement), announcement, queue.Options.MaxPerClient, handBack: null, cancellationToken))
        {
            logger.LogInformation("Client '{ClientRefId}' is offline with a full queue; command '{Type}' not queued.", command.ClientRefId, command.Type);
            return new ClientCommandResult(ClientCommandOutcome.QueueFull, target.ClientId);
        }

        logger.LogInformation("Client '{ClientRefId}' is offline; command '{CommandId}' ('{Type}') queued until {ExpiresAt:O}.", command.ClientRefId, command.CommandId, command.Type, expiresAt);
        return new ClientCommandResult(ClientCommandOutcome.Queued, target.ClientId, ExpiresAtUtc: expiresAt);
    }
}

public static class ClientCommandServiceCollectionExtensions
{
    /// <summary>
    /// The one way a host sends a command to a client, and the queue commands for an offline client
    /// wait in. Needs the platform database and the bus.
    /// </summary>
    public static IServiceCollection AddClientCommandSender(this IServiceCollection services)
    {
        services.AddScoped<IClientCommandTargetProvider, ClientCommandTargetProvider>();
        services.AddScoped<IQueuedCommandStore, QueuedCommandStore>();
        services.AddScoped<ClientCommandQueue>();
        services.AddScoped<IClientCommandSender, ClientCommandSender>();
        services.AddOptions<CommandQueueOptions>().BindConfiguration(CommandQueueOptions.SectionName);
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
