using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Auth;

/// <summary>
/// A workspace was deleted in the auth database. Everything the main database holds for it -
/// registration policies, clients, workflows, schedules, webhook triggers - is retired in response
/// by the management host (<c>WorkspaceDeletedHandler</c>), since nothing there can cascade from a
/// row in another database.
/// </summary>
[EventCriticality(EventCriticality.Warning)]
public sealed record WorkspaceDeletedEvent(int WorkspaceId, int DeletedByUserId) : BaseEvent, IWorkspaceContext;
