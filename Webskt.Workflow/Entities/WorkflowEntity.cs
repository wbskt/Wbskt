namespace Webskt.Workflow.Entities;

public sealed class WorkflowEntity
{
    public int Id { get; set; }
    public Guid RefId { get; set; }
    public int WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public int Version { get; set; }
    public string DefinitionJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
