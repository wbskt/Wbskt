using System.Text.Json.Serialization;

namespace Wbskt.Models;

/// <summary>
/// The one response shape every list endpoint returns: a page of <see cref="Items"/> and the cursor
/// for the next one. Pass <see cref="NextCursor"/> back as <c>cursor</c> to read on; it is null on the
/// last page. The cursor is opaque: callers store and return it, never build or parse it.
/// </summary>
public record Page<T>
{
    // Declared in the order they were serialized when Items sat on a base record (System.Text.Json
    // writes a derived type's own properties first), so the wire order is unchanged.
    public string? NextCursor { get; init; }

    /// <summary>How many items the whole list holds, only on lists where counting is cheap.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TotalCount { get; init; }

    public required IEnumerable<T> Items { get; init; }
}
