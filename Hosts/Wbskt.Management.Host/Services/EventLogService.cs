using Wbskt.Events;
using Wbskt.Events.Abstractions;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services;

internal sealed class EventLogService : IEventLogService
{
    /// <summary>The window the summary and the CSV read when the caller names none: the console's default view.</summary>
    internal static readonly TimeSpan DefaultRange = TimeSpan.FromDays(7);

    /// <summary>Longer than any retention the log is kept for, so it never cuts off what is there.</summary>
    internal static readonly TimeSpan MaxRange = TimeSpan.FromDays(400);

    internal const int MaxExportRows = 100_000;
    internal const int MaxSearchLength = 100;

    private readonly IEventProvider _eventProvider;
    private readonly IRegistrationPolicyService _policyService;
    private readonly IClientQueryService _clientService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EventLogService> _logger;

    public EventLogService(
        IEventProvider eventProvider,
        IRegistrationPolicyService policyService,
        IClientQueryService clientService,
        ILogger<EventLogService> logger)
        : this(eventProvider, policyService, clientService, TimeProvider.System, logger)
    {
    }

    internal EventLogService(
        IEventProvider eventProvider,
        IRegistrationPolicyService policyService,
        IClientQueryService clientService,
        TimeProvider timeProvider,
        ILogger<EventLogService> logger)
    {
        _eventProvider = eventProvider;
        _policyService = policyService;
        _clientService = clientService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<Page<EventLogResponse>>> GetLogsAsync(int workspaceId, EventLogQuery query, long? cursor, int take, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying event logs for WorkspaceId: {WorkspaceId}", workspaceId);

        // The list reads all of retention unless asked for less, as it always has.
        TimeRange? range = null;
        if (query.From is not null || query.To is not null)
        {
            var resolved = TimeRange.Resolve(query.From ?? DateTimeOffset.UnixEpoch, query.To, _timeProvider.GetUtcNow(), MaxRange, TimeSpan.MaxValue);
            if (resolved.IsFailure)
            {
                return Result<Page<EventLogResponse>>.Failure(resolved.Error);
            }

            range = resolved.Value;
        }

        var filter = await ResolveFilterAsync(workspaceId, query, range, cancellationToken);
        if (filter.IsFailure)
        {
            return Result<Page<EventLogResponse>>.Failure(filter.Error);
        }

        take = Paging.Take(take);
        if (filter.Value is null)
        {
            return Result<Page<EventLogResponse>>.Success(ToPage([], take));
        }

        // One row more than the page, to learn whether there is a next one.
        var rows = await _eventProvider.GetLogsAsync(workspaceId, filter.Value, cursor, take + 1, cancellationToken);
        return Result<Page<EventLogResponse>>.Success(ToPage(rows, take));
    }

    public async Task<Result<string>> GetCsvAsync(int workspaceId, EventLogQuery query, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Exporting event logs for WorkspaceId: {WorkspaceId}", workspaceId);

        var range = TimeRange.Resolve(query.From, query.To, _timeProvider.GetUtcNow(), DefaultRange, MaxRange);
        if (range.IsFailure)
        {
            return Result<string>.Failure(range.Error);
        }

        var filter = await ResolveFilterAsync(workspaceId, query, range.Value, cancellationToken);
        if (filter.IsFailure)
        {
            return Result<string>.Failure(filter.Error);
        }

        IReadOnlyCollection<EventLogRow> rows = filter.Value is null
            ? []
            : await _eventProvider.GetLogsAsync(workspaceId, filter.Value, null, MaxExportRows, cancellationToken);
        return Result<string>.Success(EventLogCsv.Write(rows.Select(r => r.Entry)));
    }

    public async Task<Result<EventLogSummaryResponse>> GetSummaryAsync(int workspaceId, DateTimeOffset? from, DateTimeOffset? to, EventLogTraffic traffic, CancellationToken cancellationToken = default)
    {
        var range = TimeRange.Resolve(from, to, _timeProvider.GetUtcNow(), DefaultRange, MaxRange);
        if (range.IsFailure)
        {
            return Result<EventLogSummaryResponse>.Failure(range.Error);
        }

        var counts = await _eventProvider.CountAsync(workspaceId, range.Value.FromUtc, range.Value.ToUtc, cancellationToken);
        return Result<EventLogSummaryResponse>.Success(Summarize(counts, range.Value, traffic));
    }

    public async Task<Result<Page<EventLogResponse>>> GetClientCommsAsync(int workspaceId, int clientId, string? direction, long? cursor, int take,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying comms log for client ID {ClientId} in WorkspaceId: {WorkspaceId}", clientId, workspaceId);

        take = Paging.Take(take);
        var rows = await _eventProvider.GetClientCommsAsync(workspaceId, clientId, direction, cursor, take + 1, cancellationToken);
        return Result<Page<EventLogResponse>>.Success(ToPage(rows, take));
    }

    /// <summary>
    /// Turns the query into the filter the database reads. Success with null means nothing can match
    /// (groups and names that leave no event), so there is no need to ask.
    /// </summary>
    private async Task<Result<EventLogFilter?>> ResolveFilterAsync(int workspaceId, EventLogQuery query, TimeRange? range, CancellationToken cancellationToken)
    {
        var search = string.IsNullOrWhiteSpace(query.Q) ? null : query.Q.Trim();
        if (search is { Length: > MaxSearchLength })
        {
            return Result<EventLogFilter?>.Failure(Error.Validation("EVENT_LOG_SEARCH_TOO_LONG", $"'q' can be at most {MaxSearchLength} characters."));
        }

        var sources = ResolveSources(query);
        if (sources.IsFailure)
        {
            return Result<EventLogFilter?>.Failure(sources.Error);
        }

        var include = ResolveIncludedEvents(query);
        if (include.IsFailure)
        {
            return Result<EventLogFilter?>.Failure(include.Error);
        }

        IReadOnlyCollection<string>? included = include.Value;
        IReadOnlyCollection<string>? excluded = null;
        if (query.Traffic == EventLogTraffic.Exclude)
        {
            if (included is null)
            {
                excluded = DeviceTrafficAttribute.EventNames;
            }
            else
            {
                included = included.Except(DeviceTrafficAttribute.EventNames, StringComparer.Ordinal).ToList();
            }
        }

        if (included is { Count: 0 })
        {
            return Result<EventLogFilter?>.Success(null);
        }

        // A filter naming another workspace's policy or client is a 404, not an empty page.
        int? policyId = null;
        if (query.PolicyRefId is { } policyRef)
        {
            var policy = await _policyService.FindInWorkspaceAsync(workspaceId, policyRef, cancellationToken);
            if (policy.IsFailure)
            {
                return Result<EventLogFilter?>.Failure(policy.Error);
            }

            policyId = policy.Value.Id;
        }

        int? clientId = null;
        if (query.ClientRefId is { } clientRef)
        {
            var client = await _clientService.EnsureClientInWorkspaceAsync(workspaceId, clientRef, cancellationToken);
            if (client.IsFailure)
            {
                return Result<EventLogFilter?>.Failure(client.Error);
            }

            clientId = client.Value;
        }

        // A workflow or user reference is matched as stored, with no ownership check: entries are read
        // from this workspace only, so a foreign reference finds nothing, and the history of a deleted
        // workflow (which no longer resolves) is exactly what an audit wants to read.
        return Result<EventLogFilter?>.Success(new EventLogFilter(
            EventName: string.IsNullOrWhiteSpace(query.EventName) ? null : query.EventName,
            EventNames: included,
            ExcludeEventNames: excluded,
            Criticality: query.Criticality,
            MinCriticality: query.MinCriticality,
            Sources: sources.Value,
            SinceId: query.SinceId,
            PolicyId: policyId,
            ClientId: clientId,
            WorkflowRefId: query.WorkflowRefId,
            UserRefId: query.UserRefId,
            FromUtc: range?.FromUtc,
            ToUtc: range?.ToUtc,
            Search: search));
    }

    /// <summary>The events the query's groups and names keep together, or null when it names neither.</summary>
    private static Result<IReadOnlyCollection<string>?> ResolveIncludedEvents(EventLogQuery query)
    {
        var groups = Split(query.Group);
        var names = Split(query.EventNames);
        if (groups.Count == 0 && names.Count == 0)
        {
            return Result<IReadOnlyCollection<string>?>.Success(null);
        }

        var resolved = EventLogGroups.Resolve(groups);
        if (resolved.IsFailure)
        {
            return Result<IReadOnlyCollection<string>?>.Failure(resolved.Error);
        }

        var events = resolved.Value.Union(names, StringComparer.Ordinal).ToList();
        return Result<IReadOnlyCollection<string>?>.Success(events);
    }

    /// <summary>The sources the query names, or null when it names none; an unknown one is <c>EVENT_LOG_SOURCE_UNKNOWN</c>.</summary>
    private static Result<IReadOnlyCollection<EventSource>?> ResolveSources(EventLogQuery query)
    {
        var names = Split(query.Source);
        if (names.Count == 0)
        {
            return Result<IReadOnlyCollection<EventSource>?>.Success(null);
        }

        var sources = new List<EventSource>();
        foreach (var name in names)
        {
            // Names only: a number would also parse, and would name a source that does not exist.
            if (!Enum.TryParse<EventSource>(name, ignoreCase: true, out var source) || !Enum.IsDefined(source) || char.IsDigit(name[0]))
            {
                return Result<IReadOnlyCollection<EventSource>?>.Failure(Error.Validation(
                    "EVENT_LOG_SOURCE_UNKNOWN", $"Unknown source '{name}'. Use one of: {string.Join(", ", Enum.GetNames<EventSource>())}."));
            }

            sources.Add(source);
        }

        return Result<IReadOnlyCollection<EventSource>?>.Success(sources.Distinct().ToList());
    }

    /// <summary>Repeated and comma-separated values alike, trimmed, blanks dropped.</summary>
    private static List<string> Split(string[]? values) =>
        values is null
            ? []
            : values.SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Distinct(StringComparer.Ordinal).ToList();

    internal static EventLogSummaryResponse Summarize(IReadOnlyCollection<EventLogCount> counts, TimeRange range, EventLogTraffic traffic)
    {
        var byEvent = counts
            .GroupBy(c => c.EventName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Count), StringComparer.Ordinal);
        var groups = EventLogGroups.All.ToDictionary(
            g => g.Key,
            g => g.Value.Sum(name => byEvent.GetValueOrDefault(name)),
            StringComparer.Ordinal);

        var kept = counts
            .Where(c => traffic == EventLogTraffic.Include || !DeviceTrafficAttribute.EventNames.Contains(c.EventName))
            .ToList();

        return new EventLogSummaryResponse(
            range.FromUtc,
            range.ToUtc,
            kept.Sum(c => c.Count),
            groups,
            Events: kept.GroupBy(c => c.EventName, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Sum(c => c.Count), StringComparer.Ordinal),
            People: kept.Where(c => c.UserRefId is not null).GroupBy(c => c.UserRefId!.Value).ToDictionary(g => g.Key, g => g.Sum(c => c.Count)),
            Sources: kept.Where(c => c.Source is not null).GroupBy(c => c.Source!.Value.ToString()).ToDictionary(g => g.Key, g => g.Sum(c => c.Count), StringComparer.Ordinal));
    }

    /// <summary>Trims the probe row; the cursor is the last row shown, and only when the probe found more.</summary>
    internal static Page<EventLogResponse> ToPage(IReadOnlyCollection<EventLogRow> rows, int take)
    {
        var page = rows.Take(take).ToList();
        return new Page<EventLogResponse>
        {
            Items = page.Select(r => r.Entry).ToList(),
            NextCursor = rows.Count > take ? PageRequest.KeyCursor(page[^1].Id) : null
        };
    }
}
