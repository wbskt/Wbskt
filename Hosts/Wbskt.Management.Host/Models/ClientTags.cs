using System.Text.RegularExpressions;
using Wbskt.Infrastructure;

namespace Wbskt.Management.Host.Models;

/// <summary>
/// What a device tag may be. Tags are compared as stored, so every way in (setting them, filtering
/// by one) goes through <see cref="TryNormalize"/> first: trimmed and lower-cased, so "Garage" and
/// " garage" are the same tag.
/// </summary>
public static partial class ClientTags
{
    public const int MaxLength = 32;
    public const int MaxPerClient = 10;

    public static readonly Error Invalid = Error.Validation("CLIENT_TAG_INVALID",
        $"A tag is 1-{MaxLength} letters, digits, spaces, '-', '_' or '.', starting and ending with a letter or digit.");

    // Letters, digits, spaces, '-', '_' and '.', starting and ending with a letter or digit. No ',':
    // list reads join a client's tags with one (dbo.Client_GetAll and friends).
    [GeneratedRegex(@"^[\p{L}\p{N}](?:[\p{L}\p{N} _.\-]*[\p{L}\p{N}])?$")]
    private static partial Regex Shape();

    public static bool TryNormalize(string? raw, out string tag)
    {
        tag = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return tag.Length is > 0 and <= MaxLength && Shape().IsMatch(tag);
    }

    /// <summary>Splits a list read's comma-joined Tags column; null (no tags) is an empty list.</summary>
    public static IReadOnlyList<string> Split(string? joined) =>
        string.IsNullOrEmpty(joined) ? [] : joined.Split(',');
}

/// <summary>A tag in use in a workspace and how many clients carry it.</summary>
public sealed record ClientTagCount(string Tag, int ClientCount);
