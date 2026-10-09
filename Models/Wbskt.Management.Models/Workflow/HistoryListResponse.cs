using Wbskt.Models;

namespace Wbskt.Management.Models.Workflow;

/// <summary>A page of a run's history events, oldest first.</summary>
public sealed record HistoryListResponse : Page<HistoryEventDto>
{
    /// <summary>Deprecated: the same events as <c>items</c>, kept for one release while the console moves over.</summary>
    public IEnumerable<HistoryEventDto> Events => Items;
}
