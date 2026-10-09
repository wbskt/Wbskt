using Wbskt.EventBus.Abstractions;
using Wbskt.Events;
using Wbskt.Events.Auth;
using Wbskt.Events.Client;
using Wbskt.Events.Management;
using Wbskt.Events.Workflow;
using Wbskt.Infrastructure;

namespace Wbskt.Management.Host.Services;

/// <summary>
/// The audit log's views, as named sets of events. Every group but security follows the namespace
/// its events live in, so a new event joins its group without an edit here. Device traffic is never
/// in a group: it is what happened on the wire, not something anyone did.
/// </summary>
public static class EventLogGroups
{
    public const string People = "people";
    public const string Clients = "clients";
    public const string Policies = "policies";
    public const string Workflows = "workflows";
    public const string Security = "security";

    /// <summary>
    /// The warnings and errors about sign-ins, passwords, who has access, keys, PINs, deletions and
    /// blocked joins: what an owner should look at first. Listed by hand because criticality alone
    /// also takes in failed runs and commands, which are not security.
    /// </summary>
    private static readonly string[] SecurityEvents =
    [
        nameof(UserLoginFailedEvent),
        nameof(SecurityAlertEvent),
        nameof(PasswordChangedEvent),
        nameof(MemberRemovedEvent),
        nameof(MemberSuspendedEvent),
        nameof(RolePermissionsUpdatedEvent),
        nameof(WorkspaceOwnershipTransferredEvent),
        nameof(WorkspaceDeletedEvent),
        nameof(ClientSecretRotatedEvent),
        nameof(ClientDeletedEvent),
        nameof(PolicyPinRotatedEvent),
        nameof(PolicyRegistrationAttemptedOnDisabledEvent),
        nameof(PolicyRegistrationLimitReachedEvent),
        nameof(WorkflowDeletedEvent)
    ];

    /// <summary>Each group's event names, by group name.</summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> All { get; } = Build();

    /// <summary>The event names of the named groups together; an unknown name is <c>EVENT_LOG_GROUP_UNKNOWN</c>.</summary>
    public static Result<IReadOnlySet<string>> Resolve(IEnumerable<string> groups)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            if (!All.TryGetValue(group, out var events))
            {
                return Result<IReadOnlySet<string>>.Failure(Error.Validation(
                    "EVENT_LOG_GROUP_UNKNOWN",
                    $"'{group}' is not an event group. The groups are {string.Join(", ", All.Keys)}."));
            }

            names.UnionWith(events);
        }

        return Result<IReadOnlySet<string>>.Success(names);
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Build()
    {
        var traffic = DeviceTrafficAttribute.EventNames.ToHashSet(StringComparer.Ordinal);
        var events = typeof(DeviceTrafficAttribute).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsNested: false } && typeof(BaseEvent).IsAssignableFrom(t))
            .Where(t => !traffic.Contains(t.Name))
            .ToList();

        IReadOnlySet<string> InNamespace(Type marker, Func<Type, bool>? where = null) =>
            events.Where(t => t.Namespace == marker.Namespace && (where is null || where(t)))
                .Select(t => t.Name)
                .ToHashSet(StringComparer.Ordinal);

        return new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [People] = InNamespace(typeof(UserLoginSuccessEvent)),
            // Registration events live with the policy ones, but they are about the client joining.
            [Clients] = InNamespace(typeof(ClientRenamedEvent))
                .Union(InNamespace(typeof(PolicyCreatedEvent), t => t.Name.StartsWith("Client", StringComparison.Ordinal)))
                .ToHashSet(StringComparer.Ordinal),
            [Policies] = InNamespace(typeof(PolicyCreatedEvent), t => t.Name.StartsWith("Policy", StringComparison.Ordinal)),
            [Workflows] = InNamespace(typeof(WorkflowPublishedEvent)),
            [Security] = SecurityEvents.ToHashSet(StringComparer.Ordinal)
        };
    }
}
