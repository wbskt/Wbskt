using Microsoft.AspNetCore.Mvc.Filters;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Auth.Host.Controllers;

/// <summary>
/// After a successful change through the marked action (or any non-GET action of a marked controller),
/// tells the other hosts that workspace access may have changed, so the management host stops serving
/// access it cached before the change. Reads and failed requests announce nothing.
/// </summary>
/// <remarks>
/// Marking whole controllers rather than each action means a new role, group, grant or membership
/// endpoint is covered without anyone remembering to add it. An action that turns out not to change
/// access only costs the management host a few cache misses. Services also announce through
/// <c>TenantAnnouncer.AccessChanged</c>, which is what covers paths with no marked controller (staff
/// actions, account deletion); announcing twice is harmless.
/// <para>
/// The announcement names the member or workspace in the route (<see cref="ScopeOf"/>), so the
/// management host re-checks only the live feeds it can reach.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AnnouncesWorkspaceAccessChangeAttribute : Attribute, IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var executed = await next();

        var http = executed.HttpContext;
        if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method))
        {
            return;
        }

        if (executed.Exception is null && http.Response.StatusCode is >= 200 and < 300)
        {
            http.RequestServices.GetRequiredService<WorkspaceAccessChanges>().Publish(ScopeOf(context.RouteData.Values));
        }
    }

    /// <summary>
    /// Whom the route names: a member (<c>{userRef}</c>) is the one whose access moved, a workspace
    /// (<c>{workspaceRef}</c>) reaches its members. A route naming neither, such as a role's
    /// permissions, may reach anyone in the tenant.
    /// </summary>
    internal static AccessChange ScopeOf(RouteValueDictionary route)
    {
        if (Guid.TryParse(route["userRef"]?.ToString(), out var userRef))
        {
            return AccessChange.ForUsers(userRef);
        }

        return Guid.TryParse(route["workspaceRef"]?.ToString(), out var workspaceRef)
            ? AccessChange.ForWorkspaces(workspaceRef)
            : AccessChange.All;
    }
}
