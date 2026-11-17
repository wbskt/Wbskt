using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Wbskt.Auth.Models;

/// <summary>
/// A username a reader cannot mistake for someone else's: letters of one alphabet, ASCII digits, and
/// '.', '_', '-' or a single space between them. A Cyrillic "a" (U+0430) among Latin letters reads as
/// a Latin one, so "admin" spelt with it would pass for an administrator in a member list. Chinese,
/// Japanese and Korean count as one alphabet, since a name mixes their scripts.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class UsernameAttribute : ValidationAttribute
{
    public UsernameAttribute()
        : base("A username is letters from one alphabet, digits, and '.', '_', '-' or single spaces between them.")
    {
    }

    public override bool IsValid(object? value) => value is not string username || IsWellFormed(username);

    /// <summary>Whether the name keeps to the rule above, read as it displays (composed, NFC).</summary>
    public static bool IsWellFormed(string username)
    {
        var name = username.Normalize(NormalizationForm.FormC);
        if (name.Length == 0 || !char.IsLetterOrDigit(name[0]) || !char.IsLetterOrDigit(name[^1]))
        {
            return false;
        }

        Alphabet? alphabet = null;
        var previous = 'a';
        foreach (var c in name)
        {
            if (c is '.' or '_' or '-' or ' ')
            {
                // One separator at a time: "a  b" and "a._b" are two names that read as one.
                if (!char.IsLetterOrDigit(previous))
                {
                    return false;
                }
            }
            else if (char.IsLetter(c))
            {
                var of = AlphabetOf(c);
                if (alphabet is { } seen && seen != of)
                {
                    return false;
                }

                alphabet = of;
            }
            else if (!char.IsAsciiDigit(c))
            {
                // Other digits (Arabic-Indic, fullwidth), marks, symbols, emoji and surrogates.
                return false;
            }

            previous = c;
        }

        return true;
    }

    private static Alphabet AlphabetOf(char c) => (int)c switch
    {
        <= 0x02AF or (>= 0x1E00 and <= 0x1EFF) or (>= 0x2C60 and <= 0x2C7F) or (>= 0xA720 and <= 0xA7FF) => Alphabet.Latin,
        (>= 0x0370 and <= 0x03FF) or (>= 0x1F00 and <= 0x1FFF) => Alphabet.Greek,
        (>= 0x0400 and <= 0x052F) or (>= 0x1C80 and <= 0x1C8F) or (>= 0x2DE0 and <= 0x2DFF) or (>= 0xA640 and <= 0xA69F) => Alphabet.Cyrillic,
        >= 0x0530 and <= 0x058F => Alphabet.Armenian,
        >= 0x13A0 and <= 0x13FF => Alphabet.Cherokee,
        _ => Alphabet.Other
    };

    // Latin, Greek, Cyrillic, Armenian and Cherokee share look-alike letters, so each is its own; the
    // rest are told apart only from those, which is what impersonating a Latin name needs.
    private enum Alphabet
    {
        Latin,
        Greek,
        Cyrillic,
        Armenian,
        Cherokee,
        Other
    }
}
