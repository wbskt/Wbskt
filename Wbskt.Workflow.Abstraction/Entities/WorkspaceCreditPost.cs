namespace Wbskt.Workflow.Abstraction.Entities;

/// <summary>What one post (<c>dbo.RunCredits_Post</c>) moved into a workspace's credit ledger.</summary>
/// <param name="Runs">How many of the workspace's runs posted.</param>
/// <param name="Credits">How many credits they moved.</param>
public sealed record WorkspaceCreditPost(int WorkspaceId, int Runs, decimal Credits);
