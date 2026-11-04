using Wbskt.Models;

namespace Wbskt.Management.Models.Workflow;

/// <summary>A page of a run's history events, oldest first.</summary>
public sealed record HistoryListResponse : Page<HistoryEventDto>;
