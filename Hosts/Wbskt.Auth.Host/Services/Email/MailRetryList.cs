namespace Wbskt.Auth.Host.Services.Email;

/// <summary>A message the relay has not taken yet, and how many times it has been tried.</summary>
internal sealed record PendingMail(OutboundMail Mail, int Attempts);

/// <summary>
/// Mail the relay failed to take, each held until it is due to be tried again. Kept in memory like
/// <see cref="OutboundMailQueue"/>, for the same reason, and used only by
/// <see cref="OutboundMailDispatcher"/>'s one loop, so it takes no lock.
/// </summary>
internal sealed class MailRetryList
{
    internal const int Capacity = OutboundMailQueue.Capacity;

    private readonly PriorityQueue<PendingMail, DateTimeOffset> _waiting = new();

    public int Count => _waiting.Count;

    /// <summary>Holds the message until <paramref name="dueAt"/>, or returns false when the list is full.</summary>
    public bool TryAdd(PendingMail mail, DateTimeOffset dueAt)
    {
        if (_waiting.Count >= Capacity)
        {
            return false;
        }

        _waiting.Enqueue(mail, dueAt);
        return true;
    }

    /// <summary>Takes the message due soonest, when it is due by <paramref name="now"/>.</summary>
    public bool TryTakeDue(DateTimeOffset now, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out PendingMail? mail)
    {
        if (_waiting.TryPeek(out _, out var dueAt) && dueAt <= now)
        {
            mail = _waiting.Dequeue();
            return true;
        }

        mail = null;
        return false;
    }

    /// <summary>How long until the next message is due; null when none waits.</summary>
    public TimeSpan? NextDueIn(DateTimeOffset now) =>
        _waiting.TryPeek(out _, out var dueAt) ? (dueAt > now ? dueAt - now : TimeSpan.Zero) : null;
}
