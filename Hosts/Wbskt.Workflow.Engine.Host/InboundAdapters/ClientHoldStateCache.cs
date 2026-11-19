using System.Collections.Concurrent;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

/// <summary>
/// The hold triggers whose state this leader last wrote as clear, so a message that does not match
/// one of them is not another serializable write that changes nothing. With it, the newest message
/// time each trigger has been given, written or skipped, so a skipped message still counts as the
/// newest reading: a matching message older than it is dropped, as the write would have dropped it
/// had the skipped one been written. The engine is the only writer
/// of hold state and only its leader records, so what it wrote stays true until it writes again: the
/// ticker moves a hold from holding to fired, or deletes it, and neither makes a clear one hold.
/// </summary>
/// <remarks>
/// Empty at start, so each trigger's first message after a takeover is written and learns its state.
/// Dropped with the trigger registration cache when a workflow changes. Writes for one trigger are
/// serialized here, so the state kept is the state the last write left, whatever order two messages
/// for it finish in.
/// </remarks>
public sealed class ClientHoldStateCache
{
    private const int LockStripes = 64;

    private readonly ConcurrentDictionary<string, (bool Clear, DateTime NewestEventAt)> _known = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, LockStripes).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    /// <summary>
    /// Records one message for the trigger, alone among messages for it: drops it when it is older than
    /// a message the trigger has already been given, skips the write when it does not match and the
    /// trigger is known to be clear, and otherwise writes and keeps the state the write returns.
    /// </summary>
    public async Task RecordAsync(string triggerKey, bool matches, DateTime eventAt, Func<CancellationToken, Task<string?>> write, CancellationToken ct)
    {
        SemaphoreSlim gate = _locks[(StringComparer.Ordinal.GetHashCode(triggerKey) & int.MaxValue) % LockStripes];
        await gate.WaitAsync(ct);
        try
        {
            if (_known.TryGetValue(triggerKey, out (bool Clear, DateTime NewestEventAt) known))
            {
                if (eventAt < known.NewestEventAt)
                {
                    return;
                }

                if (!matches && known.Clear)
                {
                    _known[triggerKey] = (true, eventAt);
                    return;
                }
            }

            string? state = await write(ct);
            _known[triggerKey] = (string.Equals(state, ClearState, StringComparison.Ordinal), eventAt);
        }
        catch
        {
            // What the write did is unknown, so the next message for the trigger writes again.
            _known.TryRemove(triggerKey, out _);
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Clear() => _known.Clear();

    /// <summary>The stored word for a trigger whose filter is not being held.</summary>
    public const string ClearState = "clear";
}
