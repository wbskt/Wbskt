namespace Wbskt.Infrastructure;

/// <summary>
/// The versioned public API. Every route served on <c>api.${DOMAIN}</c> lives under
/// <see cref="Prefix"/>, except the ones in <see cref="Unversioned"/>.
/// </summary>
public static class PublicApi
{
    public const string Prefix = "api/v1";

    /// <summary>
    /// First path segments under <c>api/</c> that are not versioned: <c>internal</c> is never public,
    /// <c>auth</c> is the console's sign-in on <c>auth.${DOMAIN}</c> (its refresh cookie is scoped to
    /// that path), and <c>callbacks</c> and <c>inbound</c> are URLs users paste into third parties,
    /// which have to keep working as they are.
    /// </summary>
    public static readonly IReadOnlySet<string> Unversioned = new HashSet<string>(StringComparer.Ordinal)
    {
        "v1", "internal", "auth", "callbacks", "inbound",
    };
}
