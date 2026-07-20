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
