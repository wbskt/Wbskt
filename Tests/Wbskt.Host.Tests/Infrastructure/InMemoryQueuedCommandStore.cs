using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure.Devices;

namespace Wbskt.Host.Tests.Infrastructure;

/// <summary>dbo.QueuedCommands in memory, with the procedures' rules: the limit, idempotent ids, take-once reads.</summary>
internal sealed class InMemoryQueuedCommandStore : IQueuedCommandStore
{
    private readonly List<QueuedCommand> _rows = [];
    private long _sequence;

    public IReadOnlyList<QueuedCommand> Rows => _rows;

    /// <summary>Client ids <see cref="EnqueueAsync"/> treats as deleted.</summary>
    public HashSet<int> MissingClients { get; } = [];

    public Task<bool> EnqueueAsync(QueuedCommand command, int maxPerClient, CancellationToken ct)
    {
        if (_rows.Any(r => r.CommandId == command.CommandId))
        {
            return Task.FromResult(true);
        }

        if (MissingClients.Contains(command.ClientId) || _rows.Count(r => r.ClientId == command.ClientId) >= maxPerClient)
        {
            return Task.FromResult(false);
        }

        _rows.Add(command with { Sequence = ++_sequence, QueuedAtUtc = command.QueuedAtUtc == default ? DateTime.UtcNow : command.QueuedAtUtc });
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<QueuedCommand>> TakeAsync(int clientId, DateTime nowUtc, CancellationToken ct) =>
        Take(r => r.ClientId == clientId && r.ExpiresAtUtc > nowUtc);

    public Task<IReadOnlyList<QueuedCommand>> TakeExpiredAsync(DateTime nowUtc, int take, CancellationToken ct) =>
        Take(r => r.ExpiresAtUtc <= nowUtc, take);

    public async Task<QueuedCommand?> CancelAsync(int clientId, Guid commandId, CancellationToken ct) =>
        (await Take(r => r.ClientId == clientId && r.CommandId == commandId)).SingleOrDefault();

    public Task<IReadOnlyList<QueuedCommand>> DropAsync(int clientId, CancellationToken ct) => Take(r => r.ClientId == clientId);

    public Task<IReadOnlyList<QueuedCommand>> GetAsync(int clientId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<QueuedCommand>>([.. _rows.Where(r => r.ClientId == clientId).OrderBy(r => r.Sequence)]);

    private Task<IReadOnlyList<QueuedCommand>> Take(Func<QueuedCommand, bool> match, int take = int.MaxValue)
    {
        var taken = _rows.Where(match).OrderBy(r => r.Sequence).Take(take).ToList();
        _rows.RemoveAll(taken.Contains);
        return Task.FromResult<IReadOnlyList<QueuedCommand>>(taken);
    }
}

/// <summary>The real sender and queue over test doubles of the database and the bus.</summary>
internal static class TestDeviceCommands
{
    public static DeviceCommandQueue Queue(IQueuedCommandStore store, IClientCommandTargetProvider targets, IEventBus bus, TimeProvider? clock = null, CommandQueueOptions? options = null) =>
        new(store, targets, bus, Options.Create(options ?? new CommandQueueOptions()), clock ?? TimeProvider.System, NullLogger<DeviceCommandQueue>.Instance);

    public static DeviceCommandSender Sender(IClientCommandTargetProvider targets, IEventBus bus, IQueuedCommandStore? store = null, TimeProvider? clock = null, CommandQueueOptions? options = null) =>
        new(targets, Queue(store ?? new InMemoryQueuedCommandStore(), targets, bus, clock, options), bus, NullLogger<DeviceCommandSender>.Instance);
}
