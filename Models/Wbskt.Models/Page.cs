using System.Text.Json.Serialization;

namespace Wbskt.Models;

/// <summary>
/// The one response shape every list endpoint returns: a page of <see cref="ListResponse{T}.Items"/>
/// and the cursor for the next one. Pass <see cref="NextCursor"/> back as <c>cursor</c> to read on; it
/// is null on the last page. The cursor is opaque: callers store and return it, never build or parse it.
/// </summary>
public record Page<T> : ListResponse<T>
{
    public string? NextCursor { get; init; }

    /// <summary>How many items the whole list holds, only on lists where counting is cheap.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TotalCount { get; init; }
}
