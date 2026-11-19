using System.Text.Json;

namespace Wbskt.Infrastructure.Security;

/// <summary>
/// Whose access may have changed: some users, every member of some workspaces, some sign-in sessions,
/// or, when the change can't be narrowed (a role's permissions, a tenant frozen, a message from a host
/// that names nothing), everyone. A listener holding access per connection re-checks only what it touches.
/// </summary>
public sealed class AccessChange
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private AccessChange(IReadOnlySet<Guid> users, IReadOnlySet<Guid> workspaces, IReadOnlySet<Guid> sessions, bool everyone)
    {
        Users = users;
        Workspaces = workspaces;
        Sessions = sessions;
        Everyone = everyone;
    }

    public static AccessChange All { get; } = new(new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>(), everyone: true);

    public bool Everyone { get; }

    public IReadOnlySet<Guid> Users { get; }

    public IReadOnlySet<Guid> Workspaces { get; }

    public IReadOnlySet<Guid> Sessions { get; }

    public static AccessChange ForUsers(params Guid[] userRefs) => new(userRefs.ToHashSet(), new HashSet<Guid>(), new HashSet<Guid>(), everyone: false);

    public static AccessChange ForWorkspaces(params Guid[] workspaceRefs) => new(new HashSet<Guid>(), workspaceRefs.ToHashSet(), new HashSet<Guid>(), everyone: false);

    public static AccessChange ForSession(Guid sessionId) => new(new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid> { sessionId }, everyone: false);

    /// <summary>Both changes at once, as a pass that runs later checks everything either touched.</summary>
    public AccessChange Union(AccessChange other) =>
        Everyone || other.Everyone
            ? All
            : new(Users.Union(other.Users).ToHashSet(), Workspaces.Union(other.Workspaces).ToHashSet(), Sessions.Union(other.Sessions).ToHashSet(), everyone: false);

    /// <summary>
    /// Whether this change can reach a holder of a token for <paramref name="userRef"/> in
    /// <paramref name="sessionId"/> that reads <paramref name="workspaceRefs"/>. An unknown user or
    /// session is reached by any change naming users or sessions, since nothing rules it out.
    /// </summary>
    public bool Touches(Guid? userRef, Guid? sessionId, IEnumerable<Guid> workspaceRefs) =>
        Everyone
        || (Users.Count > 0 && (userRef is not { } user || Users.Contains(user)))
        || (Sessions.Count > 0 && (sessionId is not { } session || Sessions.Contains(session)))
        || (Workspaces.Count > 0 && workspaceRefs.Any(Workspaces.Contains));

    /// <summary>The message announcing it; sessions travel on the revocation channel instead.</summary>
    internal string ToMessage() =>
        Everyone ? "1" : JsonSerializer.Serialize(new Message(Users.ToArray(), Workspaces.ToArray()), Json);

    /// <summary>
    /// Reads an announcement. Anything that is not a scope this host understands, the "1" an auth host
    /// before #1001 sends included, means everyone: re-checking too much is never wrong.
    /// </summary>
    internal static AccessChange Parse(string? message)
    {
        if (string.IsNullOrEmpty(message) || message[0] != '{')
        {
            return All;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Message>(message, Json);
            var users = parsed?.Users ?? [];
            var workspaces = parsed?.Workspaces ?? [];
            return users.Length + workspaces.Length == 0
                ? All
                : new(users.ToHashSet(), workspaces.ToHashSet(), new HashSet<Guid>(), everyone: false);
        }
        catch (JsonException)
        {
            return All;
        }
    }

    private sealed record Message(Guid[]? Users, Guid[]? Workspaces);
}
