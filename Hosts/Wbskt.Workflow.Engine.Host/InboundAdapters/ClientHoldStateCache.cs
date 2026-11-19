using System.Collections.Concurrent;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

/// <summary>
/// The hold triggers whose state this leader last wrote as clear, so a message that does not match
/// one of them is not another serializable write that changes nothing. The engine is the only writer
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

    private readonly ConcurrentDictionary<string, bool> _clear = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, LockStripes).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    /// <summary>
    /// Records one message for the trigger, alone among messages for it: skips the write when the
    /// message does not match and the trigger is known to be clear, and otherwise writes and keeps the
    /// state the write returns.
    /// </summary>
    public async Task RecordAsync(string triggerKey, bool matches, Func<CancellationToken, Task<string?>> write, CancellationToken ct)
    {
        SemaphoreSlim gate = _locks[(StringComparer.Ordinal.GetHashCode(triggerKey) & int.MaxValue) % LockStripes];
        await gate.WaitAsync(ct);
        try
        {
            if (!matches && _clear.TryGetValue(triggerKey, out bool clear) && clear)
            {
                return;
            }

            string? state = await write(ct);
            _clear[triggerKey] = string.Equals(state, ClearState, StringComparison.Ordinal);
        }
        catch
        {
            // What the write did is unknown, so the next message for the trigger writes again.
            _clear.TryRemove(triggerKey, out _);
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Clear() => _clear.Clear();

    /// <summary>The stored word for a trigger whose filter is not being held.</summary>
    public const string ClearState = "clear";
}
