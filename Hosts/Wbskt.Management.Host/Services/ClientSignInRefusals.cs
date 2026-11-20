using Wbskt.Events.Client;

namespace Wbskt.Management.Host.Services;

/// <summary>
/// The refused sign-ins of known clients since the last <see cref="Take"/>, one entry per client with its
/// count, so <see cref="ClientSignInRefusalReporter"/> raises one <see cref="ClientSignInRefusedEvent"/> a
/// minute per client however fast a device retries. Holds at most <see cref="MaxClients"/> clients; past
/// that, a new client's refusals wait for the next minute's room, while ones already held keep counting.
/// </summary>
public sealed class ClientSignInRefusals
{
    internal const int MaxClients = 10_000;

    private readonly Lock _gate = new();
    private Dictionary<int, Tally> _pending = new();

    public void Record(int clientId, Guid clientRefId, int workspaceId, ClientSignInRefusal reason, DateTimeOffset at)
    {
        lock (_gate)
        {
            if (_pending.TryGetValue(clientId, out Tally? tally))
            {
                tally.Attempts++;
                tally.Reason = reason;
                tally.LastAt = at;
                return;
            }

            if (_pending.Count < MaxClients)
            {
                _pending[clientId] = new Tally(clientRefId, workspaceId, reason, at);
            }
        }
    }

    /// <summary>Every client's refusals since the last call, as the events to raise, and starts counting afresh.</summary>
    public IReadOnlyCollection<ClientSignInRefusedEvent> Take()
    {
        Dictionary<int, Tally> taken;
        lock (_gate)
        {
            if (_pending.Count == 0)
            {
                return [];
            }

            taken = _pending;
            _pending = new Dictionary<int, Tally>();
        }

        return taken
            .Select(p => new ClientSignInRefusedEvent(p.Value.ClientRefId, p.Key, p.Value.WorkspaceId, p.Value.Reason, p.Value.Attempts, p.Value.FirstAt, p.Value.LastAt))
            .ToList();
    }

    private sealed class Tally(Guid clientRefId, int workspaceId, ClientSignInRefusal reason, DateTimeOffset at)
    {
        public Guid ClientRefId { get; } = clientRefId;
        public int WorkspaceId { get; } = workspaceId;
        public ClientSignInRefusal Reason { get; set; } = reason;
        public int Attempts { get; set; } = 1;
        public DateTimeOffset FirstAt { get; } = at;
        public DateTimeOffset LastAt { get; set; } = at;
    }
}
