using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Wbskt.Infrastructure.Security;

/// <summary>
/// The one claims contract for every token the hosts issue (#306): <c>sub</c> is the principal's
/// RefId, <c>typ</c> says whether that is a user or a device, and a user token carries no internal
/// id. A host that needs a user's internal id resolves it from the RefId (<see cref="IUserIdLookup"/>).
/// </summary>
/// <remarks>
/// <para>
/// Claims are written under their short JWT names. Readers here look under the short name and the
/// name the bearer handler maps it to (<c>sub</c> becomes <see cref="ClaimTypes.NameIdentifier"/>,
/// <c>sid</c> becomes <see cref="ClaimTypes.Sid"/>), so the answer does not depend on whether inbound
/// claim mapping is on.
/// </para>
/// <para>
/// Tokens issued before this contract (user: <c>sub</c> the internal id, <c>uref</c> the RefId,
/// <c>type</c>; device: <c>type</c>) are still read for one token lifetime. The legacy readers go in
/// the release after.
/// </para>
/// <para>
/// A device token still carries the device's internal id and workspace id (<c>id</c>,
/// <c>workspace_id</c>): the socket host has no data source of its own to resolve them from, and it
/// stamps them on every event it publishes. The token is held by the device it names, so it reveals
/// only that device's own row.
/// </para>
/// </remarks>
public static class WbsktClaims
{
    public const string Subject = JwtRegisteredClaimNames.Sub;
    public const string Type = "typ";
    public const string Session = "sid";
    public const string Name = "name";
    public const string Email = "email";

    public const string UserType = "user";
    public const string ClientType = "client";

    /// <summary>Before #306: the principal type.</summary>
    internal const string LegacyType = "type";

    /// <summary>Before #306: a user token's RefId, when <c>sub</c> was the internal id.</summary>
    internal const string LegacyUserRef = "uref";

    /// <summary>The claims of a user's access token.</summary>
    public static Claim[] ForUser(Guid userRef, string username, string email, Guid sessionId) =>
    [
        new(Subject, userRef.ToString()),
        new(Type, UserType),
        new(Name, username),
        new(Email, email),
        new(Session, sessionId.ToString())
    ];

    /// <summary>The value of <paramref name="type"/>, under its short name or the name the bearer handler maps it to.</summary>
    public static string? Find(ClaimsPrincipal principal, string type)
    {
        string? mapped = type switch
        {
            Subject => ClaimTypes.NameIdentifier,
            Session => ClaimTypes.Sid,
            Name => ClaimTypes.Name,
            Email => ClaimTypes.Email,
            _ => null
        };

        return principal.FindFirst(type)?.Value ?? (mapped is null ? null : principal.FindFirst(mapped)?.Value);
    }

    /// <summary>What the token is for: <see cref="UserType"/>, <see cref="ClientType"/>, or null.</summary>
    public static string? TypeOf(ClaimsPrincipal principal) =>
        principal.FindFirst(Type)?.Value ?? principal.FindFirst(LegacyType)?.Value;

    /// <summary>The session a user token belongs to.</summary>
    public static Guid? SessionOf(ClaimsPrincipal principal) =>
        Guid.TryParse(Find(principal, Session), out var sessionId) ? sessionId : null;

    /// <summary>
    /// Reads a user token: the user's RefId, and for a token issued before #306 the internal id it
    /// still carries (null for a current one, whose host resolves it).
    /// </summary>
    public static bool TryReadUser(ClaimsPrincipal principal, out Guid userRef, out int? legacyUserId)
    {
        legacyUserId = null;
        userRef = Guid.Empty;
        if (TypeOf(principal) != UserType)
        {
            return false;
        }

        string? subject = Find(principal, Subject);
        if (Guid.TryParse(subject, out userRef))
        {
            return true;
        }

        if (int.TryParse(subject, out int userId) && Guid.TryParse(principal.FindFirst(LegacyUserRef)?.Value, out userRef))
        {
            legacyUserId = userId;
            return true;
        }

        return false;
    }
}

/// <summary>A user's internal id from their RefId, for a host that keys its own records by it.</summary>
public interface IUserIdLookup
{
    /// <summary>Null when there is no such user. Throws when the answer cannot be had right now.</summary>
    Task<int?> FindUserIdAsync(Guid userRef, CancellationToken cancellationToken);
}

/// <summary>
/// Remembers what <see cref="IUserIdLookup"/> answered. A RefId names one user for good, so a found
/// id is kept for an hour after its last use; a miss is not kept, so a user created a moment later
/// is found.
/// </summary>
public sealed class CachedUserIdLookup(IUserIdLookup inner, IMemoryCache cache) : IUserIdLookup
{
    internal static readonly TimeSpan SlidingExpiration = TimeSpan.FromHours(1);

    public async Task<int?> FindUserIdAsync(Guid userRef, CancellationToken cancellationToken)
    {
        var key = (typeof(CachedUserIdLookup), userRef);
        if (cache.TryGetValue(key, out int cached))
        {
            return cached;
        }

        int? userId = await inner.FindUserIdAsync(userRef, cancellationToken);
        if (userId is > 0)
        {
            cache.Set(key, userId.Value, new MemoryCacheEntryOptions { SlidingExpiration = SlidingExpiration });
        }

        return userId;
    }
}

public static class UserIdLookupServiceCollectionExtensions
{
    /// <summary>How this host turns a token's user RefId into its internal id, cached.</summary>
    public static IServiceCollection AddUserIdLookup(this IServiceCollection services, Func<IServiceProvider, IUserIdLookup> lookup)
    {
        services.AddMemoryCache();
        services.AddScoped<IUserIdLookup>(sp => new CachedUserIdLookup(lookup(sp), sp.GetRequiredService<IMemoryCache>()));
        return services;
    }
}
