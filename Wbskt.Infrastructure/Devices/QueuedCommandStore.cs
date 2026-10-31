using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Events.Abstractions;

namespace Wbskt.Infrastructure.Devices;

/// <summary>A command waiting in a device's queue, with who sent it.</summary>
public sealed record QueuedCommand(
    Guid CommandId,
    int ClientId,
    Guid ClientRefId,
    int WorkspaceId,
    string Type,
    string Payload,
    DateTime ExpiresAtUtc,
    DateTime QueuedAtUtc = default,
    int? ActorUserId = null,
    Guid? ActorUserRefId = null,
    EventSource? ActorSource = null,
    Guid? ActorWorkflowRefId = null,
    Guid? ActorRunRefId = null)
{
    /// <summary>The order commands were queued in, for delivering oldest first.</summary>
    public long Sequence { get; init; }

    /// <summary>The waiting command's sender, as the actor of an event about it.</summary>
    public void Attribute(ActorEvent @event)
    {
        @event.ActorUserId = ActorUserId;
        @event.ActorUserRefId = ActorUserRefId;
        @event.ActorSource = ActorSource;
        @event.ActorWorkflowRefId = ActorWorkflowRefId;
        @event.ActorRunRefId = ActorRunRefId;
    }

    /// <summary>A command to queue, with the actor named on <paramref name="sentBy"/>.</summary>
    public static QueuedCommand From(DeviceCommand command, int clientId, DateTime expiresAtUtc, ActorEvent sentBy) => new(
        command.CommandId,
        clientId,
        command.ClientRefId,
        command.WorkspaceId,
        command.Type,
        command.Payload,
        expiresAtUtc,
        ActorUserId: sentBy.ActorUserId,
        ActorUserRefId: sentBy.ActorUserRefId,
        ActorSource: sentBy.ActorSource,
        ActorWorkflowRefId: sentBy.ActorWorkflowRefId,
        ActorRunRefId: sentBy.ActorRunRefId);
}

/// <summary>What became of a command handed to <see cref="IQueuedCommandStore.EnqueueAsync"/>.</summary>
public enum EnqueueOutcome : byte
{
    /// <summary>The device already has as many commands waiting as it may, or no longer exists.</summary>
    Refused = 0,

    Queued = 1,

    /// <summary>A command with this id was already waiting; nothing changed.</summary>
    AlreadyQueued = 2
}

/// <summary>
/// The commands waiting for offline devices. Every read that ends a wait takes the rows out, so a
/// command is delivered, expired, cancelled or dropped once, by whichever caller takes it.
/// </summary>
public interface IQueuedCommandStore
{
    /// <summary>Queues the command, unless the device already has <paramref name="maxPerClient"/> waiting.</summary>
    Task<EnqueueOutcome> EnqueueAsync(QueuedCommand command, int maxPerClient, CancellationToken ct);

    /// <summary>Takes the device's unexpired commands, oldest first, for delivery.</summary>
    Task<IReadOnlyList<QueuedCommand>> TakeAsync(int clientId, DateTime nowUtc, CancellationToken ct);

    /// <summary>Takes up to <paramref name="take"/> commands whose expiry has passed.</summary>
    Task<IReadOnlyList<QueuedCommand>> TakeExpiredAsync(DateTime nowUtc, int take, CancellationToken ct);

    /// <summary>Takes one waiting command of the device's, or null when it is not waiting.</summary>
    Task<QueuedCommand?> CancelAsync(int clientId, Guid commandId, CancellationToken ct);

    /// <summary>Takes every command the device has waiting.</summary>
    Task<IReadOnlyList<QueuedCommand>> DropAsync(int clientId, CancellationToken ct);

    /// <summary>The device's waiting commands, oldest first; nothing is taken.</summary>
    Task<IReadOnlyList<QueuedCommand>> GetAsync(int clientId, CancellationToken ct);
}

internal sealed class QueuedCommandStore : BaseSqlProvider, IQueuedCommandStore
{
    public QueuedCommandStore(IConfiguration configuration) : base(configuration) { }

    public async Task<EnqueueOutcome> EnqueueAsync(QueuedCommand command, int maxPerClient, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.QueuedCommand_Enqueue",
            p =>
            {
                p.AddWithValue("@CommandId", command.CommandId);
                p.AddWithValue("@ClientId", command.ClientId);
                p.AddWithValue("@ClientRefId", command.ClientRefId);
                p.AddWithValue("@WorkspaceId", command.WorkspaceId);
                p.AddWithValue("@Type", command.Type);
                p.AddWithValue("@Payload", command.Payload);
                p.AddWithValue("@ExpiresAt", command.ExpiresAtUtc);
                p.AddWithValue("@ActorUserId", (object?)command.ActorUserId ?? DBNull.Value);
                p.AddWithValue("@ActorUserRefId", (object?)command.ActorUserRefId ?? DBNull.Value);
                p.AddWithValue("@ActorSource", command.ActorSource is { } source ? (object)(byte)source : DBNull.Value);
                p.AddWithValue("@ActorWorkflowRefId", (object?)command.ActorWorkflowRefId ?? DBNull.Value);
                p.AddWithValue("@ActorRunRefId", (object?)command.ActorRunRefId ?? DBNull.Value);
                p.AddWithValue("@MaxPerClient", maxPerClient);
            },
            reader => (EnqueueOutcome)reader.GetByte(reader.GetOrdinal("Outcome")),
            cancellationToken: ct);
    }

    public async Task<IReadOnlyList<QueuedCommand>> TakeAsync(int clientId, DateTime nowUtc, CancellationToken ct)
    {
        var taken = await ExecuteCollectionAsync(
            "dbo.QueuedCommand_TakeBy_Client",
            p =>
            {
                p.AddWithValue("@ClientId", clientId);
                p.AddWithValue("@Now", nowUtc);
            },
            Map,
            ct);
        return [.. taken.OrderBy(c => c.Sequence)];
    }

    public async Task<IReadOnlyList<QueuedCommand>> TakeExpiredAsync(DateTime nowUtc, int take, CancellationToken ct)
    {
        var taken = await ExecuteCollectionAsync(
            "dbo.QueuedCommand_TakeExpired",
            p =>
            {
                p.AddWithValue("@Now", nowUtc);
                p.AddWithValue("@Take", take);
            },
            Map,
            ct);
        return [.. taken.OrderBy(c => c.Sequence)];
    }

    public Task<QueuedCommand?> CancelAsync(int clientId, Guid commandId, CancellationToken ct)
    {
        return ExecuteFindAsync(
            "dbo.QueuedCommand_Cancel",
            p =>
            {
                p.AddWithValue("@ClientId", clientId);
                p.AddWithValue("@CommandId", commandId);
            },
            Map,
            ct);
    }

    public async Task<IReadOnlyList<QueuedCommand>> DropAsync(int clientId, CancellationToken ct)
    {
        var dropped = await ExecuteCollectionAsync("dbo.QueuedCommand_DeleteBy_Client", p => p.AddWithValue("@ClientId", clientId), Map, ct);
        return [.. dropped.OrderBy(c => c.Sequence)];
    }

    public async Task<IReadOnlyList<QueuedCommand>> GetAsync(int clientId, CancellationToken ct)
    {
        return [.. await ExecuteCollectionAsync("dbo.QueuedCommand_GetBy_Client", p => p.AddWithValue("@ClientId", clientId), Map, ct)];
    }

    private static QueuedCommand Map(SqlDataReader reader)
    {
        return new QueuedCommand(
            reader.GetGuid(reader.GetOrdinal("CommandId")),
            reader.GetInt32(reader.GetOrdinal("ClientId")),
            reader.GetGuid(reader.GetOrdinal("ClientRefId")),
            reader.GetInt32(reader.GetOrdinal("WorkspaceId")),
            reader.GetString(reader.GetOrdinal("Type")),
            reader.GetString(reader.GetOrdinal("Payload")),
            DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("ExpiresAt")), DateTimeKind.Utc),
            DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("QueuedAt")), DateTimeKind.Utc),
            NullableInt(reader, "ActorUserId"),
            NullableGuid(reader, "ActorUserRefId"),
            reader.IsDBNull(reader.GetOrdinal("ActorSource")) ? null : (EventSource)reader.GetByte(reader.GetOrdinal("ActorSource")),
            NullableGuid(reader, "ActorWorkflowRefId"),
            NullableGuid(reader, "ActorRunRefId"))
        {
            Sequence = reader.GetInt64(reader.GetOrdinal("Id"))
        };
    }

    private static int? NullableInt(SqlDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static Guid? NullableGuid(SqlDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }
}
