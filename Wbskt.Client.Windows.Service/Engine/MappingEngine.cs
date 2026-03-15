using Wbskt.Client.Windows.Models;

namespace Wbskt.Client.Windows.Service.Engine;

public class MappingEngine
{
    private readonly Dictionary<ActionType, IActionHandler> _handlers;
    private List<CommandMapping> _mappings = new();

    public MappingEngine(IEnumerable<IActionHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.Type);
    }

    public void UpdateMappings(List<CommandMapping> mappings)
    {
        _mappings = mappings;
    }

    public async Task ProcessCommandAsync(string commandName, string jsonPayload)
    {
        var activeMappings = _mappings.Where(m => m.CommandName == commandName && m.IsEnabled);

        foreach (var mapping in activeMappings)
        {
            if (!_handlers.TryGetValue(mapping.ActionType, out var handler))
            {
                continue;
            }

            var resolvedParams = mapping.Parameters.ToDictionary(
                p => p.Key, 
                p => VariableResolver.Resolve(p.Value, jsonPayload)
            );

            try
            {
                await handler.ExecuteAsync(resolvedParams);
            }
            catch (Exception ex)
            {
                // TODO: Log error to local log or send back as telemetry
                Console.WriteLine($"Error executing action {mapping.ActionType}: {ex.Message}");
            }
        }
    }
}
