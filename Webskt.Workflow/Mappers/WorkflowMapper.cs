using System.Text.Json;
using Webskt.Workflow.Abstraction.Models;
using Webskt.Workflow.Entities;

namespace Webskt.Workflow.Mappers;

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
