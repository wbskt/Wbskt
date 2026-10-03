namespace Wbskt.Management.Host.Models;

public class ClientStateVariable
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string ValueJson { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Outcome of one state-variable upsert: the previous value, or <c>Stored = false</c> when the
/// client is at its variable cap and a new name was refused.</summary>
public sealed record StateVariableUpsert(bool Stored, string? OldValueJson);
