using Wbskt.Models;

namespace Wbskt.Api.Client;

/// <summary>
/// The filters <c>GET event-logs</c> and <c>GET event-logs/csv</c> take, as query parameters. Every
/// one narrows the result and they combine with AND, except that <see cref="Groups"/> and
/// <see cref="EventNames"/> together keep an event named by either. A reference to another
/// workspace's client or policy is a 404, never an empty page.
/// </summary>
public sealed record EventLogSearch
{
    /// <summary>Entries whose event name contains this.</summary>
    public string? EventName { get; init; }

    /// <summary>Exact event names.</summary>
    public IReadOnlyList<string>? EventNames { get; init; }

    /// <summary>Named groups of events: people, clients, policies, workflows, security.</summary>
    public IReadOnlyList<string>? Groups { get; init; }

    public EventCriticality? Criticality { get; init; }

    /// <summary>This criticality or worse: <c>Warning</c> is warnings and errors.</summary>
    public EventCriticality? MinCriticality { get; init; }

    /// <summary>Where entries came from: Console, Api, Device, Workflow, System.</summary>
    public IReadOnlyList<string>? Sources { get; init; }

    /// <summary>Only entries newer than this id: the newest one a live view already shows.</summary>
    public long? SinceId { get; init; }

    public Guid? PolicyRefId { get; init; }

    public Guid? ClientRefId { get; init; }

    public Guid? WorkflowRefId { get; init; }

    /// <summary>Only entries this user caused (or, for a sign-in, is about).</summary>
    public Guid? UserRefId { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    /// <summary>Leave raw device traffic (messages, pings, readings) out.</summary>
    public bool ExcludeDeviceTraffic { get; init; }

    /// <summary>Text to find in an entry's data or event name.</summary>
    public string? Q { get; init; }

    internal IEnumerable<(string Name, object? Value)> ToQuery()
    {
        yield return ("eventName", EventName);
        yield return ("eventNames", EventNames);
        yield return ("group", Groups);
        yield return ("criticality", Criticality);
        yield return ("minCriticality", MinCriticality);
        yield return ("source", Sources);
        yield return ("sinceId", SinceId);
        yield return ("policyRefId", PolicyRefId);
        yield return ("clientRefId", ClientRefId);
        yield return ("workflowRefId", WorkflowRefId);
        yield return ("userRefId", UserRefId);
        yield return ("from", From);
        yield return ("to", To);
        yield return ("traffic", ExcludeDeviceTraffic ? "exclude" : null);
        yield return ("q", Q);
    }
}
