using Wbskt.Models;

namespace Wbskt.Management.Models.Workflow;

/// <summary>A page of runs, newest first.</summary>
public sealed record RunListResponse : Page<RunSummaryDto>;
