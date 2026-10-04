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
/// access only costs the management host a few cache misses.
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
            http.RequestServices.GetRequiredService<WorkspaceAccessChanges>().Publish();
        }
    }
}
