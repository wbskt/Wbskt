namespace Wbskt.Workflow.Abstraction.Entities;

/// <summary>
/// A triggered run with the root branch it was created with. <see cref="Created"/> is false when the
/// run already existed under the same trigger start key, so this attempt recovered it rather than
/// starting it.
/// </summary>
public sealed record RunStartRow(RunRow Run, long RootBranchId, bool Created);
