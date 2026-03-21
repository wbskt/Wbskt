namespace Wbskt.Models;

public record ListResponse<T>
{
    public required IEnumerable<T> Items { get; init; }
}
