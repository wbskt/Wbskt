using Wbskt.Models;

namespace Wbskt.Management.Models.Workflow;

/// <summary>A page of runs, newest first.</summary>
public sealed record RunListResponse : Page<RunSummaryDto>
{
    /// <summary>Deprecated: the same runs as <c>items</c>, kept for one release while the console moves over.</summary>
    public IEnumerable<RunSummaryDto> Runs => Items;
}
