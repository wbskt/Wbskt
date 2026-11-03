using System.Text;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// Names nobody may sign up with or rename to, so nobody can pose as staff in a workspace. Applies to
/// look-alikes too: case, separators, digits standing in for letters, and the product's name anywhere
/// in it (<c>Admin_</c>, <c>adm1n</c>, <c>the.support.team</c>, <c>wbskt-help</c>).
/// </summary>
internal static class ReservedUsernames
{
    public const string Brand = "wbskt";

    private static readonly HashSet<string> Reserved =
    [
        "admin", "administrator", "root", "support", "system", "sysadmin", "superuser", "staff",
        "moderator", "security", "help", "helpdesk", "official", "postmaster", "abuse", "noreply",
        "billing", "owner"
    ];

    /// <summary>Wrappers that do not make a reserved name someone's own (<c>supportteam</c>, <c>theadmin</c>).</summary>
    private static readonly string[] Affixes = ["the", "team", "staff", "official", "real"];

    public static bool IsReserved(string username)
    {
        var name = Normalise(username);
        if (name.Length == 0)
        {
            return false;
        }

        if (name.Contains(Brand, StringComparison.Ordinal))
        {
            return true;
        }

        var core = name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        if (Reserved.Contains(core) || Reserved.Contains(Deleet(core)) || Reserved.Contains(Deleet(name)))
        {
            return true;
        }

        var bare = StripAffixes(Deleet(name));
        return Reserved.Contains(bare);
    }

    // Lower case, letters and digits only: "Admin_", "ad.min" and "ADMIN" are one name to a reader.
    private static string Normalise(string username)
    {
        var sb = new StringBuilder(username.Length);
        foreach (var c in username.Normalize(NormalizationForm.FormKD))
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }

    private static string Deleet(string name) => string.Create(name.Length, name, (span, source) =>
    {
        for (var i = 0; i < source.Length; i++)
        {
            span[i] = source[i] switch
            {
                '0' => 'o',
                '1' => 'i',
                '3' => 'e',
                '4' => 'a',
                '5' => 's',
                '7' => 't',
                _ => source[i]
            };
        }
    });

    private static string StripAffixes(string name)
    {
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var affix in Affixes)
            {
                if (name.Length > affix.Length && name.StartsWith(affix, StringComparison.Ordinal))
                {
                    name = name[affix.Length..];
                    changed = true;
                }

                if (name.Length > affix.Length && name.EndsWith(affix, StringComparison.Ordinal))
                {
                    name = name[..^affix.Length];
                    changed = true;
                }
            }
        }

        return name;
    }
}
