namespace Wbskt.Management.Host.Services;

/// <summary>
/// The rule client and policy names share: trimmed, and 1 to 100 characters, the width of their
/// columns. Checked here because SQL would cut a longer name to fit without a word.
/// </summary>
internal static class EntityNames
{
    public const int MaxLength = 100;

    /// <summary>The name trimmed, or null when it is blank or too long.</summary>
    public static string? Normalize(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength ? null : trimmed;
    }
}
