using Wbskt.Workflow.Abstraction.Integrations;

namespace Wbskt.Management.Host.Models;

/// <summary>A row of dbo.Integrations. The secret stays sealed: only the engine can open it.</summary>
public sealed class Integration : IWorkspaceOwned
{
    public int Id { get; init; }
    public Guid RefId { get; init; }
    public int WorkspaceId { get; init; }
    public IntegrationType Type { get; init; }
    public IntegrationPreset? Preset { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ConfigJson { get; init; } = "{}";
    public byte[] SecretCiphertext { get; init; } = [];
    public byte KeyId { get; init; }
    public string? SecretHint { get; init; }
    public string? Metadata { get; init; }
    public DateTime? LastTestAt { get; init; }
    public bool? LastTestOk { get; init; }
    public string? LastTestCode { get; init; }
    public string? LastTestError { get; init; }
    public DateTime? LastUsedAt { get; init; }
    public int CreatedBy { get; init; }
    public Guid? CreatedByRefId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime? SecretRotatedAt { get; init; }
    public int UsageWorkflows { get; init; }
    public int UsageSteps { get; init; }
}

/// <summary>A step of a live workflow's current version that sends through an integration.</summary>
public sealed record IntegrationUse(Guid WorkflowRefId, string WorkflowName, Guid NodeId, string NodeName, bool IsEnabled);
