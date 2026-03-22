using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Entities;

namespace Wbskt.Workflow.Mappers;

public static class WorkflowMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static WorkflowDefinition? ToDefinition(this WorkflowEntity entity)
    {
        if (string.IsNullOrWhiteSpace(entity.DefinitionJson))
        {
            return null;
        }

        try
        {
            var definition = JsonSerializer.Deserialize<WorkflowDefinition>(entity.DefinitionJson, JsonOptions);
            if (definition == null)
            {
                return null;
            }

            // Sync DB-level fields (Source of Truth) into the object
            definition.WorkflowRefId = entity.RefId;
            definition.WorkflowId = entity.Id;
            definition.WorkspaceId = entity.WorkspaceId;
            definition.Name = entity.Name;
            definition.Description = entity.Description;
            definition.IsEnabled = entity.IsEnabled;
            definition.Version = entity.Version;
            definition.CreatedAt = entity.CreatedAt;

            return definition;
        }
        catch
        {
            return null;
        }
    }
}
