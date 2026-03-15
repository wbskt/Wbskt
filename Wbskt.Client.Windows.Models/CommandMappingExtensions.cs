namespace Wbskt.Client.Windows.Models;

public static class CommandMappingExtensions
{
    public static CommandMappingDto ToDto(this CommandMapping m) => new(
        Id:          m.Id,
        CommandName: m.CommandName,
        FriendlyName: m.FriendlyName,
        ActionType:  m.ActionType.ToString(),
        IsEnabled:   m.IsEnabled,
        CreatedAt:   m.CreatedAt,
        Parameters:  new Dictionary<string, string>(m.Parameters)
    );

    public static CommandMapping ToModel(this CommandMappingDto dto) => new(dto.Id)
    {
        CommandName  = dto.CommandName,
        FriendlyName = dto.FriendlyName,
        ActionType   = Enum.Parse<ActionType>(dto.ActionType, ignoreCase: true),
        IsEnabled    = dto.IsEnabled,
        CreatedAt    = dto.CreatedAt,
        Parameters   = new Dictionary<string, string>(dto.Parameters)
    };
}